#!/usr/bin/env bash
# Private PulseAudio null source for the virtual-device test.
# Source this file from the shell that runs the test.
# Execute it with --self-check to prove teardown without starting PulseAudio.
set -euo pipefail

# /proc/<pid>/stat field 22 is the process start time. The command field may
# contain spaces, so the field is taken from the text after the last ")".
microsharp_proc_snapshot() {
  local pid="$1"
  [[ "$pid" =~ ^[1-9][0-9]*$ ]] || return 1
  [[ -r "/proc/${pid}/stat" ]] || return 1
  awk '
    {
      n = split($0, parts, ")")
      if (n < 2) {
        exit 1
      }
      rest = parts[n]
      sub(/^[[:space:]]+/, "", rest)
      count = split(rest, fields, /[[:space:]]+/)
      if (count < 20 || fields[1] == "" || fields[20] !~ /^[0-9]+$/) {
        exit 1
      }
      printf "%s %s\n", fields[1], fields[20]
    }
  ' "/proc/${pid}/stat"
}

microsharp_runtime_is_private() {
  local runtime="$1"
  local tmp="${TMPDIR:-}"
  local parent base
  [[ -n "$tmp" && -n "$runtime" ]] || return 1
  [[ "$runtime" != *".."* ]] || return 1
  [[ -d "$runtime" && ! -L "$runtime" ]] || return 1
  parent="$(dirname -- "$runtime")"
  base="$(basename -- "$runtime")"
  [[ "$parent" == "${tmp%/}" ]] || return 1
  [[ "$base" == microsharp-pulse.* ]] || return 1
  return 0
}

microsharp_wait_released() {
  local pid="$1"
  local start="$2"
  local tries="$3"
  local _ snap state now
  for _ in $(seq 1 "$tries"); do
    if ! snap="$(microsharp_proc_snapshot "$pid")"; then
      return 0
    fi
    state="${snap%% *}"
    now="${snap#* }"
    if [[ "$now" != "$start" || "$state" == "Z" ]]; then
      return 0
    fi
    sleep 0.1
  done
  return 1
}

# Signal only when pid and start time still name the same live process.
# A zombie is already exited: do not signal it and do not signal its parent.
# Returns 0 when that process is no longer a running daemon, 1 when it still is.
microsharp_signal_matching() {
  local pid="$1"
  local start="$2"
  local snap state now
  if ! snap="$(microsharp_proc_snapshot "$pid")"; then
    return 0
  fi
  state="${snap%% *}"
  now="${snap#* }"
  if [[ -z "$start" || "$now" != "$start" ]]; then
    echo "refusing to stop process ${pid}; its start identity does not match" >&2
    return 1
  fi
  if [[ "$state" == "Z" ]]; then
    return 0
  fi
  kill -TERM "$pid" 2>/dev/null || true
  if microsharp_wait_released "$pid" "$start" 20; then
    return 0
  fi
  if ! snap="$(microsharp_proc_snapshot "$pid")"; then
    return 0
  fi
  state="${snap%% *}"
  now="${snap#* }"
  if [[ "$now" != "$start" || "$state" == "Z" ]]; then
    return 0
  fi
  kill -KILL "$pid" 2>/dev/null || true
  sleep 0.1
  if ! snap="$(microsharp_proc_snapshot "$pid")"; then
    return 0
  fi
  state="${snap%% *}"
  now="${snap#* }"
  if [[ "$now" != "$start" || "$state" == "Z" ]]; then
    return 0
  fi
  return 1
}

microsharp_virtual_capture_stop() {
  local runtime="${MICROSHARP_PULSE_RUNTIME:-}"
  local pid="${MICROSHARP_PULSE_PID:-}"
  local start="${MICROSHARP_PULSE_START:-}"
  local server="${MICROSHARP_PULSE_SERVER:-}"
  local gone=0
  local snap state now

  if [[ -z "$runtime" && -z "$pid" && -z "$server" ]]; then
    return 0
  fi
  if ! microsharp_runtime_is_private "$runtime"; then
    echo "refusing to clean an unexpected capture runtime" >&2
    return 1
  fi
  if [[ -z "$pid" && -f "$runtime/owner-pid" ]]; then
    pid="$(tr -cd '0-9' < "$runtime/owner-pid")"
  fi
  if [[ -z "$start" && -f "$runtime/owner-start" ]]; then
    start="$(tr -cd '0-9' < "$runtime/owner-start")"
  fi
  if [[ -z "$server" && -f "$runtime/owner-server" ]]; then
    IFS= read -r server < "$runtime/owner-server" || true
  fi

  if [[ -z "$pid" ]] || ! snap="$(microsharp_proc_snapshot "$pid")"; then
    gone=1
  else
    state="${snap%% *}"
    now="${snap#* }"
    if [[ -n "$start" && "$now" == "$start" && "$state" == "Z" ]]; then
      echo "private capture process ${pid} has exited" >&2
      gone=1
    elif [[ -z "$start" || "$now" != "$start" ]]; then
      echo "refusing to stop process ${pid}; its start identity does not match" >&2
      return 1
    fi
  fi

  if [[ "$gone" -eq 0 && "$server" == "unix:${runtime}/"* ]]; then
    if command -v pactl >/dev/null 2>&1; then
      pactl --server="$server" exit >/dev/null 2>&1 || true
    fi
    if microsharp_wait_released "$pid" "$start" 20; then
      echo "stopped private capture daemon ${pid} through its exit request"
      gone=1
    fi
  fi

  if [[ "$gone" -eq 0 ]]; then
    if ! microsharp_signal_matching "$pid" "$start"; then
      echo "private capture process ${pid} is still running" >&2
      return 1
    fi
    echo "stopped private capture process ${pid} after its start identity matched"
  fi

  rm -rf -- "$runtime"
  unset MICROSHARP_PULSE_PID MICROSHARP_PULSE_START MICROSHARP_PULSE_SERVER
  unset MICROSHARP_PULSE_RUNTIME MICROSHARP_PULSE_PIDFILE
  unset XDG_RUNTIME_DIR PULSE_RUNTIME_PATH PULSE_STATE_PATH PULSE_SERVER
  return 0
}

# Report whether the recorded daemon is still running. This sends no signal.
microsharp_virtual_capture_assert_gone() {
  local pid="${1:-}"
  local start="${2:-}"
  local snap state now
  if [[ ! "$pid" =~ ^[1-9][0-9]*$ ]]; then
    return 0
  fi
  if ! snap="$(microsharp_proc_snapshot "$pid")"; then
    return 0
  fi
  state="${snap%% *}"
  now="${snap#* }"
  if [[ "$state" == "Z" ]]; then
    return 0
  fi
  if [[ -n "$start" && "$now" != "$start" ]]; then
    return 0
  fi
  echo "private capture process ${pid} is still running" >&2
  return 1
}

microsharp_capture_log() {
  local runtime="${MICROSHARP_PULSE_RUNTIME:-}"
  if [[ -n "$runtime" && -f "$runtime/state/pulse.log" ]]; then
    cat "$runtime/state/pulse.log" >&2 || true
  fi
}

microsharp_virtual_capture_self_check() {
  local tmp="${TMPDIR:?TMPDIR must be set for the teardown self-check}"
  local saved_path="$PATH"
  local fixture_list="" keep_dir="" shim_dir="" pactl_log=""
  local link_name="" link_target="" unexpected_dir="" zombie_dir=""
  local runtime="" line="" pid="" start="" wrong="" before=0 after=0
  local holder_pid="" holder_start="" zombie_pid="" zombie_start="" snap=""
  local foreign_pid="" foreign_start=""

  microsharp_track_fixture() {
    printf '%s %s\n' "$1" "$2" >> "$fixture_list"
  }

  microsharp_start_sleep() {
    local sleep_pid="" sleep_snap="" plain="" _
    bash -c 'exec sleep 300' </dev/null >/dev/null 2>&1 &
    sleep_pid=$!
    sleep_snap=""
    for _ in 1 2 3 4 5 6 7 8 9 10; do
      if sleep_snap="$(microsharp_proc_snapshot "$sleep_pid")"; then
        break
      fi
      sleep 0.05
    done
    if [[ -z "$sleep_snap" ]]; then
      echo "fixture ${sleep_pid} did not start" >&2
      return 1
    fi
    plain="$(awk '{print $22}' "/proc/${sleep_pid}/stat")"
    if [[ "${sleep_snap#* }" != "$plain" ]]; then
      echo "start time parser disagreed for ${sleep_pid}" >&2
      return 1
    fi
    printf '%s %s\n' "$sleep_pid" "${sleep_snap#* }"
  }

  microsharp_self_check_finish() {
    local item_pid="" item_start="" item_snap="" item_state="" item_now="" leaked=0
    PATH="$saved_path"
    hash -r || true
    if [[ -n "$fixture_list" && -f "$fixture_list" ]]; then
      while read -r item_pid item_start; do
        [[ -n "${item_pid:-}" && -n "${item_start:-}" ]] || continue
        if ! microsharp_signal_matching "$item_pid" "$item_start"; then
          leaked=1
        fi
        if item_snap="$(microsharp_proc_snapshot "$item_pid" 2>/dev/null)"; then
          item_state="${item_snap%% *}"
          item_now="${item_snap#* }"
          if [[ "$item_state" != "Z" && "$item_now" == "$item_start" ]]; then
            leaked=1
          fi
        fi
      done < "$fixture_list"
      while read -r item_pid item_start; do
        [[ "${item_pid:-}" =~ ^[1-9][0-9]*$ ]] || continue
        wait "$item_pid" 2>/dev/null || true
      done < "$fixture_list"
    fi
    if [[ -n "$link_name" && -L "$link_name" ]]; then
      rm -- "$link_name"
    fi
    if [[ -n "$link_target" ]]; then
      rm -rf -- "$link_target"
    fi
    if [[ -n "$unexpected_dir" ]]; then
      rm -rf -- "$unexpected_dir"
    fi
    if [[ -n "$keep_dir" ]]; then
      rm -rf -- "$keep_dir"
    fi
    if [[ -n "$shim_dir" ]]; then
      rm -rf -- "$shim_dir"
    fi
    if [[ -n "$zombie_dir" ]]; then
      rm -rf -- "$zombie_dir"
    fi
    unset MICROSHARP_PULSE_PID MICROSHARP_PULSE_START MICROSHARP_PULSE_SERVER
    unset MICROSHARP_PULSE_RUNTIME MICROSHARP_PULSE_PIDFILE MICROSHARP_PACTL_LOG
    return "$leaked"
  }

  fixture_list="$(mktemp "${tmp%/}/microsharp-fixtures.XXXXXX")"
  keep_dir="$(mktemp -d "${tmp%/}/microsharp-unrelated.XXXXXX")"
  unexpected_dir="$(mktemp -d "${tmp%/}/microsharp-unrelated.XXXXXX")"
  echo keep > "$keep_dir/marker"
  echo keep > "$unexpected_dir/marker"
  shim_dir="$(mktemp -d "${tmp%/}/microsharp-pactl-shim.XXXXXX")"
  pactl_log="$shim_dir/log"
  : > "$pactl_log"
  cat > "$shim_dir/pactl" << 'EOF'
#!/bin/bash
printf '%s\n' "$*" >> "${MICROSHARP_PACTL_LOG:?}"
exit 0
EOF
  chmod 700 "$shim_dir/pactl"
  export MICROSHARP_PACTL_LOG="$pactl_log"
  PATH="${shim_dir}:${PATH}"
  hash -r || true
  code=0
  trap 'code=$?; microsharp_self_check_finish || true; exit "$code"' EXIT

  export MICROSHARP_PULSE_RUNTIME="$unexpected_dir"
  unset MICROSHARP_PULSE_PID MICROSHARP_PULSE_START MICROSHARP_PULSE_SERVER || true
  if microsharp_virtual_capture_stop; then
    echo "teardown accepted an unexpected runtime" >&2
    return 1
  fi
  if [[ ! -f "$unexpected_dir/marker" ]]; then
    echo "teardown removed an unexpected runtime" >&2
    return 1
  fi

  link_target="$(mktemp -d "${tmp%/}/microsharp-link-target.XXXXXX")"
  echo keep > "$link_target/marker"
  link_name="${tmp%/}/microsharp-pulse.$(mktemp -u XXXXXX)"
  ln -s "$link_target" "$link_name"
  export MICROSHARP_PULSE_RUNTIME="$link_name"
  if microsharp_virtual_capture_stop; then
    echo "teardown followed a runtime symlink" >&2
    return 1
  fi
  if [[ ! -f "$link_target/marker" ]]; then
    echo "teardown removed the target of a runtime symlink" >&2
    return 1
  fi

  if ! line="$(microsharp_start_sleep)"; then
    return 1
  fi
  foreign_pid="${line%% *}"
  foreign_start="${line#* }"
  microsharp_track_fixture "$foreign_pid" "$foreign_start"
  runtime="$(mktemp -d "${tmp%/}/microsharp-pulse.XXXXXX")"
  if ! line="$(microsharp_start_sleep)"; then
    return 1
  fi
  pid="${line%% *}"
  start="${line#* }"
  microsharp_track_fixture "$pid" "$start"
  export MICROSHARP_PULSE_RUNTIME="$runtime"
  export MICROSHARP_PULSE_PID="$pid"
  export MICROSHARP_PULSE_START="$start"
  unset MICROSHARP_PULSE_SERVER || true
  if ! microsharp_virtual_capture_stop; then
    echo "owned-process teardown failed" >&2
    return 1
  fi
  if snap="$(microsharp_proc_snapshot "$pid" 2>/dev/null)" && [[ "${snap%% *}" != "Z" ]]; then
    echo "owned process survived" >&2
    return 1
  fi
  if [[ -d "$runtime" ]]; then
    echo "runtime directory survived" >&2
    return 1
  fi
  if ! snap="$(microsharp_proc_snapshot "$foreign_pid")" || [[ "${snap#* }" != "$foreign_start" || "${snap%% *}" == "Z" ]]; then
    echo "a process outside the private runtime was stopped" >&2
    return 1
  fi
  if ! microsharp_virtual_capture_stop; then
    echo "a second teardown was not idempotent" >&2
    return 1
  fi
  runtime="$(mktemp -d "${tmp%/}/microsharp-pulse.XXXXXX")"
  if ! line="$(microsharp_start_sleep)"; then
    return 1
  fi
  pid="${line%% *}"
  start="${line#* }"
  microsharp_track_fixture "$pid" "$start"
  if [[ "$start" == "0" ]]; then
    wrong="1"
  else
    wrong="0"
  fi
  export MICROSHARP_PULSE_RUNTIME="$runtime"
  export MICROSHARP_PULSE_PID="$pid"
  export MICROSHARP_PULSE_START="$wrong"
  unset MICROSHARP_PULSE_SERVER || true
  if microsharp_virtual_capture_stop; then
    echo "teardown accepted a process whose start identity does not match" >&2
    return 1
  fi
  if ! snap="$(microsharp_proc_snapshot "$pid")"; then
    echo "teardown stopped a process whose start identity does not match" >&2
    return 1
  fi
  if [[ "${snap#* }" != "$start" || "${snap%% *}" == "Z" ]]; then
    echo "teardown changed a process whose start identity does not match" >&2
    return 1
  fi
  if ! snap="$(microsharp_proc_snapshot "$foreign_pid")" || [[ "${snap#* }" != "$foreign_start" ]]; then
    echo "a process outside the private runtime was stopped" >&2
    return 1
  fi
  export MICROSHARP_PULSE_START=""
  if microsharp_virtual_capture_stop; then
    echo "teardown accepted a process with no recorded start identity" >&2
    return 1
  fi
  if ! snap="$(microsharp_proc_snapshot "$pid")" || [[ "${snap#* }" != "$start" ]]; then
    echo "teardown stopped a process with no recorded start identity" >&2
    return 1
  fi
  export MICROSHARP_PULSE_START="$start"
  if ! microsharp_virtual_capture_stop; then
    echo "cleanup with the recorded start identity failed" >&2
    return 1
  fi

  runtime="$(mktemp -d "${tmp%/}/microsharp-pulse.XXXXXX")"
  if ! line="$(microsharp_start_sleep)"; then
    return 1
  fi
  pid="${line%% *}"
  start="${line#* }"
  microsharp_track_fixture "$pid" "$start"
  before="$(wc -l < "$pactl_log")"
  export MICROSHARP_PULSE_RUNTIME="$runtime"
  export MICROSHARP_PULSE_PID="$pid"
  export MICROSHARP_PULSE_START="$start"
  export MICROSHARP_PULSE_SERVER="unix:${keep_dir}/native"
  if ! microsharp_virtual_capture_stop; then
    echo "teardown failed for an outside server path" >&2
    return 1
  fi
  after="$(wc -l < "$pactl_log")"
  if [[ "$before" -ne "$after" ]]; then
    echo "teardown sent an exit request outside its runtime" >&2
    return 1
  fi
  if snap="$(microsharp_proc_snapshot "$pid" 2>/dev/null)" && [[ "${snap%% *}" != "Z" ]]; then
    echo "owned process survived an outside server path" >&2
    return 1
  fi

  runtime="$(mktemp -d "${tmp%/}/microsharp-pulse.XXXXXX")"
  if ! line="$(microsharp_start_sleep)"; then
    return 1
  fi
  pid="${line%% *}"
  start="${line#* }"
  microsharp_track_fixture "$pid" "$start"
  export MICROSHARP_PULSE_RUNTIME="$runtime"
  export MICROSHARP_PULSE_PID="$pid"
  export MICROSHARP_PULSE_START="$start"
  export MICROSHARP_PULSE_SERVER="unix:${runtime}/native"
  if ! microsharp_virtual_capture_stop; then
    echo "teardown failed for a private server path" >&2
    return 1
  fi
  if ! grep -F -x -- "--server=unix:${runtime}/native exit" "$pactl_log" >/dev/null; then
    echo "teardown did not request exit on the private socket" >&2
    return 1
  fi
  if snap="$(microsharp_proc_snapshot "$pid" 2>/dev/null)" && [[ "${snap%% *}" != "Z" ]]; then
    echo "owned process survived the private exit request" >&2
    return 1
  fi

  if ! command -v perl >/dev/null 2>&1; then
    echo "the zombie fixture needs perl" >&2
    return 1
  fi
  zombie_dir="$(mktemp -d "${tmp%/}/microsharp-zombie.XXXXXX")"
  cat > "$zombie_dir/hold.pl" << 'EOF'
use strict;
use warnings;
$| = 1;
my $out = $ARGV[0];
defined(my $pid = fork()) or die "fork: $!";
if ($pid == 0) {
    exit 0;
}
open my $fh, ">", $out or die $!;
print {$fh} "$pid\n";
close $fh;
sleep 120;
EOF
  perl "$zombie_dir/hold.pl" "$zombie_dir/child" </dev/null >/dev/null 2>&1 &
  holder_pid=$!
  for _ in 1 2 3 4 5 6 7 8 9 10; do
    if snap="$(microsharp_proc_snapshot "$holder_pid" 2>/dev/null)"; then
      holder_start="${snap#* }"
      break
    fi
    sleep 0.05
  done
  if [[ -z "$holder_start" ]]; then
    echo "zombie holder did not start" >&2
    return 1
  fi
  microsharp_track_fixture "$holder_pid" "$holder_start"
  for _ in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20; do
    if [[ -s "$zombie_dir/child" ]]; then
      zombie_pid="$(tr -cd '0-9' < "$zombie_dir/child")"
      if snap="$(microsharp_proc_snapshot "$zombie_pid" 2>/dev/null)" && [[ "${snap%% *}" == "Z" ]]; then
        break
      fi
    fi
    sleep 0.05
  done
  if ! snap="$(microsharp_proc_snapshot "$holder_pid")" || [[ "${snap#* }" != "$holder_start" ]]; then
    echo "zombie holder exited before the check" >&2
    return 1
  fi
  if ! snap="$(microsharp_proc_snapshot "$zombie_pid")" || [[ "${snap%% *}" != "Z" ]]; then
    echo "the fixture did not leave a zombie" >&2
    return 1
  fi
  zombie_start="${snap#* }"
  runtime="$(mktemp -d "${tmp%/}/microsharp-pulse.XXXXXX")"
  export MICROSHARP_PULSE_RUNTIME="$runtime"
  export MICROSHARP_PULSE_PID="$zombie_pid"
  export MICROSHARP_PULSE_START="$zombie_start"
  unset MICROSHARP_PULSE_SERVER || true
  if ! microsharp_virtual_capture_stop; then
    echo "teardown treated an exited zombie as a running daemon" >&2
    return 1
  fi
  if ! snap="$(microsharp_proc_snapshot "$holder_pid")" || [[ "${snap#* }" != "$holder_start" || "${snap%% *}" == "Z" ]]; then
    echo "teardown signaled the parent of an exited zombie" >&2
    return 1
  fi
  if ! snap="$(microsharp_proc_snapshot "$zombie_pid")" || [[ "${snap%% *}" != "Z" || "${snap#* }" != "$zombie_start" ]]; then
    echo "teardown changed the exited zombie" >&2
    return 1
  fi

  if [[ ! -f "$keep_dir/marker" ]]; then
    echo "teardown removed an unrelated directory" >&2
    return 1
  fi
  trap - EXIT
  if ! microsharp_self_check_finish; then
    echo "fixture cleanup left a process running" >&2
    return 1
  fi
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

runtime="$(mktemp -d "${TMPDIR%/}/microsharp-pulse.XXXXXX")"
mkdir -p "$runtime/xdg" "$runtime/pulse" "$runtime/state"
chmod 700 "$runtime" "$runtime/xdg" "$runtime/pulse" "$runtime/state"
socket="$runtime/pulse/native"
pidfile="$runtime/pulse/pid"
export XDG_RUNTIME_DIR="$runtime/xdg"
export PULSE_RUNTIME_PATH="$runtime/pulse"
export PULSE_STATE_PATH="$runtime/state"
export ALSOFT_DRIVERS=pulse
export MICROSHARP_PULSE_RUNTIME="$runtime"
export MICROSHARP_PULSE_PIDFILE="$pidfile"
unset PULSE_SERVER MICROSHARP_PULSE_SERVER MICROSHARP_PULSE_PID MICROSHARP_PULSE_START || true
code=0
trap 'code=$?; microsharp_virtual_capture_stop || code=1; exit "$code"' EXIT

# -n skips default.pa. That script loads hardware modules and aborts when the runner has none.
# Exit stays allowed so pactl can ask this private daemon to stop. --disallow-exit would block that.
if ! env -u PULSE_SERVER pulseaudio \
  --daemonize=yes \
  --exit-idle-time=-1 \
  --realtime=no \
  --high-priority=no \
  --use-pid-file=yes \
  --log-target="file:${runtime}/state/pulse.log" \
  --log-level=info \
  -n \
  --load="module-native-protocol-unix auth-anonymous=1 socket=${socket}"; then
  echo "PulseAudio did not start." >&2
  microsharp_capture_log
  return 1
fi

ready=0
for _ in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20; do
  if [[ -f "$pidfile" ]]; then
    candidate="$(tr -cd '0-9' < "$pidfile")"
    if [[ "$candidate" =~ ^[1-9][0-9]*$ ]] && snap="$(microsharp_proc_snapshot "$candidate")"; then
      comm="$(tr -cd '[:alnum:]' < "/proc/${candidate}/comm" 2>/dev/null || true)"
      if [[ "$comm" == "pulseaudio" ]]; then
        export MICROSHARP_PULSE_PID="$candidate"
        export MICROSHARP_PULSE_START="${snap#* }"
        printf '%s\n' "$MICROSHARP_PULSE_PID" > "$runtime/owner-pid"
        printf '%s\n' "$MICROSHARP_PULSE_START" > "$runtime/owner-start"
        ready=1
        break
      fi
    fi
  fi
  sleep 0.1
done
if [[ "$ready" -ne 1 ]]; then
  echo "PulseAudio did not record a start identity." >&2
  microsharp_capture_log
  return 1
fi

export PULSE_SERVER="unix:${socket}"
export MICROSHARP_PULSE_SERVER="$PULSE_SERVER"
printf '%s\n' "$PULSE_SERVER" > "$runtime/owner-server"

ready=0
for _ in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20; do
  if pactl --server="$PULSE_SERVER" info >/dev/null 2>&1; then
    ready=1
    break
  fi
  sleep 0.25
done
if [[ "$ready" -ne 1 ]]; then
  echo "PulseAudio did not become ready." >&2
  microsharp_capture_log
  return 1
fi

# module-null-source on the CI image accepts source_name and description.
# source_properties is not a supported argument there.
if ! pactl --server="$PULSE_SERVER" load-module module-null-source source_name=microsharp_null description=microsharp_null; then
  echo "The null source module did not load." >&2
  microsharp_capture_log
  return 1
fi
sources="$(pactl --server="$PULSE_SERVER" list short sources)"
printf '%s\n' "$sources"
if [[ "$sources" != *microsharp_null* ]]; then
  echo "The prepared null source is not listed." >&2
  microsharp_capture_log
  return 1
fi
