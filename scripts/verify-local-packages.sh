#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${TMPDIR:-}" ]]; then
  echo "TMPDIR must be set. Large package checks do not use the default temporary directory." >&2
  exit 1
fi

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
packages="$root/artifacts/packages"
work="$(mktemp -d "${TMPDIR%/}/microsharp-packages.XXXXXX")"
cache="$work/nuget-packages"
mkdir -p "$cache"
export NUGET_PACKAGES="$cache"
trap 'rm -rf "$work"' EXIT

dotnet pack "$root/MicroSharp.slnx" --configuration Release --output "$packages" --nologo

project="$work/consumer-net11.0"
mkdir -p "$project"
cat >"$project/consumer.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MicroSharp" Version="0.2.0" />
    <PackageReference Include="MicroSharp.Native" Version="0.2.0" />
    <PackageReference Include="MicroSharp.Processing" Version="0.2.0" />
  </ItemGroup>
</Project>
EOF
cat >"$project/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="microsharp-local" value="$packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
cat >"$project/Program.cs" <<'EOF'
using MicroSharp;
using MicroSharp.Native;
using MicroSharp.Processing;
var capture = new OpenAlMicrophoneCapture(new OpenAlMicrophoneOptions("microsharp-absent-capture-device"));
if (!capture.CapturesOperatingSystemDevice || capture.RequiresBrowserHost)
{
    return 1;
}

var format = PcmFormat.Default;
var ring = new PcmHistoryRing(format, TimeSpan.FromSeconds(1));
var pcm = new byte[format.BytesPerChunk];
ring.Write(pcm);
var destination = new byte[pcm.Length];
var read = ring.Read(ring.OpenCursor(PcmHistoryCursorOrigin.Earliest), destination);
var analysis = Pcm16LeBlockAnalyzer.Analyze(destination.AsSpan(0, read), format);
if (read != pcm.Length || analysis.DurationMilliseconds != 20d || analysis.MeanAbsoluteAmplitude != 0)
{
    return 4;
}

return capture.AdapterName == OpenAlMicrophoneCapture.AdapterId ? 0 : 1;
EOF
dotnet run --project "$project/consumer.csproj" --nologo

browser="$work/browser-lib"
mkdir -p "$browser"
cat >"$browser/browser.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MicroSharp.Browser" Version="0.2.0" />
  </ItemGroup>
</Project>
EOF
cat >"$browser/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="microsharp-local" value="$packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
cat >"$browser/ReferenceCheck.cs" <<'EOF'
using MicroSharp.Browser;
public sealed class ReferenceCheck
{
    public static bool UsesBrowserAdapter()
        => new BrowserMicrophoneCapture(new NoJs()).RequiresBrowserHost;

    private sealed class NoJs : Microsoft.JSInterop.IJSRuntime
    {
        public System.Threading.Tasks.ValueTask<TValue> InvokeAsync<TValue>(string identifier, System.Threading.CancellationToken cancellationToken, object?[]? args)
            => throw new System.NotSupportedException();

        public System.Threading.Tasks.ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new System.NotSupportedException();
    }
}
EOF
dotnet build "$browser/browser.csproj" --configuration Release --nologo

dotnet restore "$root/samples/MicroSharp.Console/MicroSharp.Console.csproj" --nologo
dotnet restore "$root/samples/MicroSharp.BlazorWasm/MicroSharp.BlazorWasm.csproj" --nologo
dotnet run --project "$root/tools/MicroSharp.ReleaseGate/MicroSharp.ReleaseGate.csproj" --configuration Release -- verify-packed \
  --packages "$packages" \
  --cache "$cache" \
  --version-file "$root/Directory.Build.props" \
  --assets "$root/samples/MicroSharp.Console/obj/project.assets.json" \
  --assets "$root/samples/MicroSharp.BlazorWasm/obj/project.assets.json"
dotnet build "$root/samples/MicroSharp.Console/MicroSharp.Console.csproj" --configuration Release --nologo
set +e
dotnet run --project "$root/samples/MicroSharp.Console/MicroSharp.Console.csproj" --configuration Release --no-build -- --device microsharp-absent-capture-device
status=$?
set -e
if [[ "$status" -ne 3 ]]; then
  echo "Expected console device-not-found exit 3, got $status" >&2
  exit 1
fi

virtual="$(dotnet run --project "$root/samples/MicroSharp.Console/MicroSharp.Console.csproj" --configuration Release --no-build -- --virtual)"
first_line="${virtual%%$'\n'*}"
if [[ "$first_line" != "640" ]]; then
  echo "Expected the first virtual line to be 640, got: $virtual" >&2
  exit 1
fi
if ! printf '%s\n' "$virtual" | grep -qx 'historyBytes=640'; then
  echo "Expected historyBytes=640, got: $virtual" >&2
  exit 1
fi
if ! printf '%s\n' "$virtual" | grep -qx 'durationMs=20'; then
  echo "Expected durationMs=20, got: $virtual" >&2
  exit 1
fi
if ! printf '%s\n' "$virtual" | grep -Eq '^meanAbsoluteAmplitude=[1-9][0-9]*$'; then
  echo "Expected a positive mean absolute amplitude, got: $virtual" >&2
  exit 1
fi

echo "Local package consumption succeeded."
