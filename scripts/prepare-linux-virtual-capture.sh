#!/usr/bin/env bash
# Private PulseAudio null source for the virtual-device test.
# Source this file from the shell that runs the test.
# Execute it with --self-check to prove owned-process teardown without starting PulseAudio.
set -euo pipefail

microsharp_virtual_capture_stop() {
  local runtime="${MICROSHARP_PULSE_RUNTIME:-}"
  local pidfile="${MICROSHARP_PULSE_PIDFILE:-}"
  local marker="${MICROSHARP_PULSE_MARKER:-}"
  local tmp="${TMPDIR:-}"
  if [[ -z "$runtime" && -z "$pidfile" ]]; then
    return 0
  fi
  if [[ -z "$tmp" || -z "$runtime" || -z "$pidfile" || "$runtime" != "${tmp%/}/microsharp-pulse-"* || "$pidfile" != "$runtime/"* ]]; then
    echo "refusing to clean an unexpected capture runtime" >&2
    return 1
  fi

  local pid=""
  if [[ -f "$pidfile" ]]; then
    pid="$(tr -cd '0-9' < "$pidfile")"
  fi
  if [[ -n "$pid" && -d "/proc/$pid" ]]; then
    local cmdline="" comm="" owned=0
    cmdline="$(tr '\0' ' ' < "/proc/$pid/cmdline" 2>/dev/null || true)"
    comm="$(tr -cd '[:alnum:]' < "/proc/$pid/comm" 2>/dev/null || true)"
    if [[ -n "$marker" && "$cmdline" == *"$marker"* ]]; then
      owned=1
    fi
    if [[ "$owned" -ne 1 && "$comm" == "pulseaudio" && -n "${PULSE_RUNTIME_PATH:-}" ]]; then
      if tr '\0' '\n' < "/proc/$pid/environ" 2>/dev/null | grep -Fxq "PULSE_RUNTIME_PATH=${PULSE_RUNTIME_PATH}"; then
        owned=1
      fi
    fi
    if [[ "$owned" -ne 1 ]]; then
      echo "refusing to stop process ${pid}; it is not the private capture daemon" >&2
      return 1
    fi
    kill -TERM "$pid" 2>/dev/null || true
    local _
    for _ in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20; do
      if [[ ! -d "/proc/$pid" ]]; then
        break
      fi
      sleep 0.1
    done
    if [[ -d "/proc/$pid" ]]; then
      kill -KILL "$pid" 2>/dev/null || true
      sleep 0.1
    fi
    if [[ -d "/proc/$pid" ]]; then
      echo "private capture process ${pid} is still running" >&2
      return 1
    fi
    echo "stopped private capture process ${pid}"
  fi

  rm -f "$pidfile"
  rm -rf "$runtime"
  unset MICROSHARP_PULSE_PID MICROSHARP_PULSE_PIDFILE MICROSHARP_PULSE_RUNTIME MICROSHARP_PULSE_MARKER
  unset XDG_RUNTIME_DIR PULSE_RUNTIME_PATH PULSE_STATE_PATH
  return 0
}

microsharp_virtual_capture_self_check() {
  local tmp="${TMPDIR:?TMPDIR must be set for the teardown self-check}"
  local owned="" foreign="" bad="" runtime="" runtime2=""
  finish() {
    local code="$1"
    if [[ -n "$foreign" ]]; then
      kill -TERM "$foreign" 2>/dev/null || true
    fi
    if [[ -n "$bad" ]]; then
      kill -TERM "$bad" 2>/dev/null || true
    fi
    if [[ -n "$owned" ]]; then
      kill -TERM "$owned" 2>/dev/null || true
    fi
    wait "$foreign" "$bad" "$owned" 2>/dev/null || true
    if [[ -n "$runtime2" ]]; then
      rm -rf "$runtime2"
    fi
    return "$code"
  }

  runtime="${tmp%/}/microsharp-pulse-self-$$"
  rm -rf "$runtime"
  mkdir -p "$runtime"
  local marker="microsharp-self-check-$$"
  local pidfile="$runtime/pid"
  bash -c 'exec -a "$1" sleep 300' _ "$marker" &
  owned="$!"
  echo "$owned" > "$pidfile"
  bash -c 'exec -a "$1" sleep 300' _ "microsharp-foreign-$$" &
  foreign="$!"
  sleep 0.1
  export MICROSHARP_PULSE_RUNTIME="$runtime"
  export MICROSHARP_PULSE_PIDFILE="$pidfile"
  export MICROSHARP_PULSE_MARKER="$marker"
  export MICROSHARP_PULSE_PID="$owned"
  export PULSE_RUNTIME_PATH="$runtime/pulse"
  if ! kill -0 "$owned" 2>/dev/null || ! kill -0 "$foreign" 2>/dev/null; then
    echo "self-check processes did not start" >&2
    finish 1 || return 1
  fi
  if ! microsharp_virtual_capture_stop; then
    echo "owned-process teardown failed" >&2
    finish 1 || return 1
  fi
  if kill -0 "$owned" 2>/dev/null; then
    echo "owned process survived" >&2
    finish 1 || return 1
  fi
  if [[ -d "$runtime" ]]; then
    echo "runtime directory survived" >&2
    finish 1 || return 1
  fi
  if ! kill -0 "$foreign" 2>/dev/null; then
    echo "a process outside the private runtime was stopped" >&2
    finish 1 || return 1
  fi
  if ! microsharp_virtual_capture_stop; then
    echo "a second teardown was not idempotent" >&2
    finish 1 || return 1
  fi

  runtime2="${tmp%/}/microsharp-pulse-foreign-$$"
  rm -rf "$runtime2"
  mkdir -p "$runtime2"
  bash -c 'exec -a "$1" sleep 300' _ "not-the-marker" &
  bad="$!"
  sleep 0.1
  echo "$bad" > "$runtime2/pid"
  export MICROSHARP_PULSE_RUNTIME="$runtime2"
  export MICROSHARP_PULSE_PIDFILE="$runtime2/pid"
  export MICROSHARP_PULSE_MARKER="microsharp-self-check-marker"
  export MICROSHARP_PULSE_PID="$bad"
  export PULSE_RUNTIME_PATH="$runtime2/pulse"
  if microsharp_virtual_capture_stop; then
    echo "teardown accepted a process it does not own" >&2
    finish 1 || return 1
  fi
  if ! kill -0 "$bad" 2>/dev/null; then
    echo "teardown stopped a process it does not own" >&2
    finish 1 || return 1
  fi
  finish 0 || return 1
  echo "private capture teardown self-check passed"
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  if [[ "${1:-}" != "--self-check" ]]; then
    echo "source this script from the test shell, or pass --self-check" >&2
    exit 2
  fi
  microsharp_virtual_capture_self_check
  exit 0
fi

if [[ -z "${TMPDIR:-}" ]]; then
  echo "TMPDIR must be set before preparing the virtual capture source." >&2
  return 1
fi

runtime="${TMPDIR%/}/microsharp-pulse-$$"
rm -rf "$runtime"
mkdir -p "$runtime/xdg" "$runtime/pulse" "$runtime/state"
chmod 700 "$runtime/xdg" "$runtime/pulse" "$runtime/state"
pidfile="$runtime/pulse/pid"
export XDG_RUNTIME_DIR="$runtime/xdg"
export PULSE_RUNTIME_PATH="$runtime/pulse"
export PULSE_STATE_PATH="$runtime/state"
export ALSOFT_DRIVERS=pulse
export MICROSHARP_PULSE_RUNTIME="$runtime"
export MICROSHARP_PULSE_PIDFILE="$pidfile"
export MICROSHARP_PULSE_MARKER="file:${runtime}/state/pulse.log"
trap 'code=$?; microsharp_virtual_capture_stop || code=1; exit "$code"' EXIT

# -n skips default.pa. That script loads hardware modules and aborts when the runner has none.
if ! pulseaudio \
  --daemonize=yes \
  --exit-idle-time=-1 \
  --disallow-exit \
  --realtime=no \
  --high-priority=no \
  --use-pid-file=yes \
  --log-target="file:${runtime}/state/pulse.log" \
  --log-level=info \
  -n \
  --load="module-native-protocol-unix auth-anonymous=1" \
  --load="module-null-source source_name=microsharp_null source_properties=device.description=microsharp_null"; then
  echo "PulseAudio did not start." >&2
  if [[ -f "$runtime/state/pulse.log" ]]; then
    cat "$runtime/state/pulse.log" >&2 || true
  fi
  return 1
fi

ready=0
for _ in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20; do
  if pactl info >/dev/null 2>&1; then
    ready=1
    break
  fi
  sleep 0.25
done
if [[ "$ready" -ne 1 ]]; then
  echo "PulseAudio did not become ready." >&2
  if [[ -f "$runtime/state/pulse.log" ]]; then
    cat "$runtime/state/pulse.log" >&2 || true
  fi
  return 1
fi

if [[ ! -f "$pidfile" ]]; then
  echo "PulseAudio did not write its pid file." >&2
  return 1
fi
export MICROSHARP_PULSE_PID
MICROSHARP_PULSE_PID="$(tr -cd '0-9' < "$pidfile")"
if [[ -z "$MICROSHARP_PULSE_PID" ]]; then
  echo "PulseAudio pid file was empty." >&2
  return 1
fi

sources="$(pactl list short sources)"
printf '%s\n' "$sources"
if [[ "$sources" != *microsharp_null* ]]; then
  echo "The prepared null source is not listed." >&2
  return 1
fi
