# =============================================================
#  🦊 inFaux Installer Script
#  https://github.com/TalviFox/inFaux
#  Modern Native Hardware Monitor & Open Telemetry Server
# =============================================================

[CmdletBinding(DefaultParameterSetName = "Default")]
param(
    [Parameter(Position = 0)]
    [string]$Path,

    [switch]$Force,
    [switch]$Build
)

# Console environment normalization (ensures 24-bit TrueColor in Windows Terminal / conhost)
try {
    if (-not ([System.Management.Automation.PSTypeName]'Win32.ConsoleVT').Type) {
        Add-Type -MemberDefinition @'
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern IntPtr GetStdHandle(int nStdHandle);
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool SetConsoleTextAttribute(IntPtr hConsoleHandle, ushort wAttributes);
'@ -Name 'ConsoleVT' -Namespace 'Win32'
    }

    $hStdOut = [Win32.ConsoleVT]::GetStdHandle(-11)
    [Win32.ConsoleVT]::SetConsoleTextAttribute($hStdOut, 0x0007) | Out-Null
    $consoleMode = 0
    if ([Win32.ConsoleVT]::GetConsoleMode($hStdOut, [ref]$consoleMode)) {
        [Win32.ConsoleVT]::SetConsoleMode($hStdOut, ($consoleMode -bor 0x0004)) | Out-Null
    }
} catch {}

try {
    $host.UI.RawUI.BackgroundColor = 'Black'
    $host.UI.RawUI.ForegroundColor = 'White'
    Clear-Host
} catch {}

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ErrorActionPreference = "Stop"

$OutputEncoding = [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$esc = [char]27
$u = [char]0x2580
$d = [char]0x2584

$guardianBanner = @"
$esc[38;2;15;33;21m$esc[49m$d$esc[38;2;19;35;21m$esc[48;2;47;59;32m$u$esc[0m $esc[38;2;103;109;112m$esc[49m$d$esc[38;2;120;126;129m$esc[49m$d$esc[38;2;109;113;119m$esc[49m$d$esc[38;2;110;116;119m$esc[49m$d$esc[38;2;110;116;123m$esc[49m$d$esc[38;2;116;122;126m$esc[49m$d$esc[38;2;119;124;127m$esc[49m$d$esc[38;2;120;124;127m$esc[49m$d$esc[38;2;120;124;127m$esc[49m$d$esc[38;2;120;123;129m$esc[49m$d$esc[38;2;120;126;129m$esc[49m$d$esc[38;2;120;124;129m$esc[49m$d$esc[38;2;120;126;129m$esc[49m$d$esc[38;2;120;126;129m$esc[49m$d$esc[38;2;120;124;129m$esc[49m$d$esc[38;2;124;129;131m$esc[49m$d$esc[38;2;130;133;134m$esc[49m$d$esc[38;2;120;126;129m$esc[49m$d$esc[38;2;123;127;130m$esc[49m$d$esc[38;2;123;129;133m$esc[49m$d$esc[38;2;134;138;143m$esc[49m$d$esc[38;2;110;115;116m$esc[49m$d$esc[0m $esc[38;2;0;26;24m$esc[49m$d$esc[0m $esc[0m   
$esc[0m $esc[0m $esc[38;2;101;103;108m$esc[48;2;119;122;126m$u$esc[38;2;157;161;169m$esc[48;2;143;148;154m$u$esc[38;2;137;141;148m$esc[48;2;179;183;189m$u$esc[38;2;151;158;164m$esc[48;2;171;176;183m$u$esc[38;2;148;154;159m$esc[48;2;176;182;186m$u$esc[38;2;148;155;162m$esc[48;2;179;185;189m$u$esc[38;2;150;157;164m$esc[48;2;180;186;190m$u$esc[38;2;150;157;161m$esc[48;2;183;189;193m$u$esc[38;2;151;158;162m$esc[48;2;185;190;194m$u$esc[38;2;151;157;164m$esc[48;2;189;196;199m$u$esc[38;2;151;158;165m$esc[48;2;192;199;203m$u$esc[38;2;151;158;162m$esc[48;2;194;200;204m$u$esc[38;2;150;157;161m$esc[48;2;197;200;206m$u$esc[38;2;151;158;164m$esc[48;2;200;206;210m$u$esc[38;2;152;158;164m$esc[48;2;206;211;215m$u$esc[38;2;151;158;162m$esc[48;2;213;218;222m$u$esc[38;2;151;158;164m$esc[48;2;214;218;222m$u$esc[38;2;151;157;166m$esc[48;2;218;222;228m$u$esc[38;2;154;159;166m$esc[48;2;224;229;234m$u$esc[38;2;155;161;166m$esc[48;2;222;227;232m$u$esc[38;2;155;161;168m$esc[48;2;222;228;232m$u$esc[38;2;145;151;155m$esc[48;2;218;222;227m$u$esc[38;2;173;178;183m$esc[48;2;158;162;166m$u$esc[38;2;110;113;117m$esc[48;2;124;127;134m$u$esc[0m $esc[0m $esc[0m   $([char]::ConvertFromUtf32(0x1F98A)) $esc[1;36minFaux$esc[0m $esc[1;33m("info")$esc[0m
$esc[0m $esc[0m $esc[38;2;112;113;117m$esc[48;2;112;116;119m$u$esc[38;2;175;182;186m$esc[48;2;171;178;183m$u$esc[38;2;164;171;175m$esc[48;2;164;169;173m$u$esc[38;2;161;166;171m$esc[48;2;164;169;173m$u$esc[38;2;161;166;171m$esc[48;2;165;171;175m$u$esc[38;2;164;171;175m$esc[48;2;169;175;179m$u$esc[38;2;168;173;178m$esc[48;2;161;166;172m$u$esc[38;2;172;178;182m$esc[48;2;110;116;120m$u$esc[38;2;180;186;190m$esc[48;2;157;162;166m$u$esc[38;2;179;185;190m$esc[48;2;190;196;200m$u$esc[38;2;186;192;196m$esc[48;2;185;190;196m$u$esc[38;2;189;194;199m$esc[48;2;192;199;203m$u$esc[38;2;193;197;203m$esc[48;2;194;199;204m$u$esc[38;2;196;201;206m$esc[48;2;194;201;204m$u$esc[38;2;200;206;208m$esc[48;2;204;211;214m$u$esc[38;2;211;217;221m$esc[48;2;196;201;206m$u$esc[38;2;213;218;222m$esc[48;2;130;136;138m$u$esc[38;2;220;225;229m$esc[48;2;204;208;214m$u$esc[38;2;221;227;232m$esc[48;2;225;232;236m$u$esc[38;2;222;227;232m$esc[48;2;222;228;232m$u$esc[38;2;221;225;231m$esc[48;2;222;227;232m$u$esc[38;2;228;234;238m$esc[48;2;228;234;238m$u$esc[38;2;210;214;221m$esc[48;2;208;214;220m$u$esc[38;2;113;113;120m$esc[48;2;112;115;120m$u$esc[0m $esc[0m $esc[0m   $esc[90mReal telemetry. Faux drivers.$esc[0m
$esc[0m $esc[0m $esc[38;2;113;115;117m$esc[48;2;112;115;117m$u$esc[38;2;172;176;182m$esc[48;2;169;176;180m$u$esc[38;2;161;166;173m$esc[48;2;161;166;171m$u$esc[38;2;164;169;173m$esc[48;2;162;169;173m$u$esc[38;2;165;172;176m$esc[48;2;165;171;175m$u$esc[38;2;169;176;180m$esc[48;2;171;178;182m$u$esc[38;2;147;152;157m$esc[48;2;140;145;150m$u$esc[38;2;145;151;155m$esc[48;2;172;179;183m$u$esc[38;2;103;108;112m$esc[48;2;143;150;154m$u$esc[38;2;155;159;164m$esc[48;2;92;98;102m$u$esc[38;2;189;194;199m$esc[48;2;151;157;161m$u$esc[38;2;185;189;193m$esc[48;2;187;193;197m$u$esc[38;2;187;192;197m$esc[48;2;189;194;199m$u$esc[38;2;196;203;207m$esc[48;2;178;185;187m$u$esc[38;2;190;194;200m$esc[48;2;102;106;110m$u$esc[38;2;124;129;133m$esc[48;2;150;154;158m$u$esc[38;2;166;172;176m$esc[48;2;207;213;217m$u$esc[38;2;183;190;194m$esc[48;2;169;175;179m$u$esc[38;2;224;229;234m$esc[48;2;222;228;232m$u$esc[38;2;225;229;235m$esc[48;2;222;228;234m$u$esc[38;2;225;231;236m$esc[48;2;227;232;236m$u$esc[38;2;225;231;235m$esc[48;2;231;236;241m$u$esc[38;2;206;211;217m$esc[48;2;207;213;218m$u$esc[38;2;110;112;117m$esc[48;2;109;112;116m$u$esc[0m $esc[0m $esc[0m   
$esc[0m $esc[38;2;70;82;85m$esc[48;2;186;190;194m$u$esc[38;2;148;152;158m$esc[48;2;144;150;154m$u$esc[38;2;157;162;168m$esc[48;2;158;164;169m$u$esc[38;2;162;169;173m$esc[48;2;164;169;173m$u$esc[38;2;159;166;169m$esc[48;2;161;168;172m$u$esc[38;2;158;164;168m$esc[48;2;161;166;171m$u$esc[38;2;162;168;175m$esc[48;2;164;172;175m$u$esc[38;2;141;145;150m$esc[48;2;154;159;164m$u$esc[38;2;158;162;168m$esc[48;2;155;159;165m$u$esc[38;2;166;172;176m$esc[48;2;168;173;178m$u$esc[38;2;123;127;133m$esc[48;2;178;185;187m$u$esc[38;2;150;152;157m$esc[48;2;189;196;200m$u$esc[38;2;183;189;193m$esc[48;2;185;190;194m$u$esc[38;2;189;193;199m$esc[48;2;183;190;194m$u$esc[38;2;171;176;179m$esc[48;2;192;197;201m$u$esc[38;2;131;136;140m$esc[48;2;197;203;207m$u$esc[38;2;185;190;192m$esc[48;2;194;201;206m$u$esc[38;2;192;197;201m$esc[48;2;178;185;186m$u$esc[38;2;173;178;182m$esc[48;2;187;193;196m$u$esc[38;2;221;227;231m$esc[48;2;214;217;222m$u$esc[38;2;221;227;231m$esc[48;2;220;227;231m$u$esc[38;2;218;224;228m$esc[48;2;221;227;231m$u$esc[38;2;224;228;234m$esc[48;2;224;229;234m$u$esc[38;2;196;201;204m$esc[48;2;194;201;204m$u$esc[38;2;148;151;157m$esc[48;2;145;152;151m$u$esc[38;2;71;81;84m$esc[48;2;186;190;193m$u$esc[0m $esc[0m   $esc[1;37mHighlights:$esc[0m
$esc[38;2;15;26;25m$esc[48;2;14;26;24m$u$esc[38;2;173;179;183m$esc[48;2;175;180;185m$u$esc[38;2;140;144;150m$esc[48;2;140;145;150m$u$esc[38;2;161;164;169m$esc[48;2;158;164;169m$u$esc[38;2;161;168;172m$esc[48;2;159;165;168m$u$esc[38;2;158;164;169m$esc[48;2;165;171;175m$u$esc[38;2;165;171;175m$esc[48;2;136;141;145m$u$esc[38;2;131;138;140m$esc[48;2;87;94;94m$u$esc[38;2;161;166;172m$esc[48;2;148;157;158m$u$esc[38;2;175;180;185m$esc[48;2;99;108;108m$u$esc[38;2;182;189;193m$esc[48;2;112;119;120m$u$esc[38;2;133;140;141m$esc[48;2;98;105;105m$u$esc[38;2;101;106;108m$esc[48;2;112;117;119m$u$esc[38;2;137;143;144m$esc[48;2;166;171;175m$u$esc[38;2;190;194;199m$esc[48;2;137;141;143m$u$esc[38;2;192;196;200m$esc[48;2;103;110;112m$u$esc[38;2;199;200;204m$esc[48;2;133;138;140m$u$esc[38;2;196;200;206m$esc[48;2;166;171;175m$u$esc[38;2;200;206;210m$esc[48;2;134;141;143m$u$esc[38;2;201;206;211m$esc[48;2;141;148;150m$u$esc[38;2;204;210;214m$esc[48;2;159;164;168m$u$esc[38;2;206;211;215m$esc[48;2;164;171;171m$u$esc[38;2;208;214;218m$esc[48;2;211;217;222m$u$esc[38;2;221;227;232m$esc[48;2;214;221;225m$u$esc[38;2;199;203;208m$esc[48;2;196;200;204m$u$esc[38;2;141;145;148m$esc[48;2;141;148;150m$u$esc[38;2;175;180;183m$esc[48;2;176;182;185m$u$esc[0m $esc[0m     $esc[32m*$esc[0m $esc[1mZero-Driver:$esc[0m 100% native user-mode ($esc[33masInvoker$esc[0m)
$esc[0m $esc[38;2;175;180;185m$esc[48;2;173;179;183m$u$esc[38;2;138;144;150m$esc[48;2;138;145;150m$u$esc[38;2;157;164;168m$esc[48;2;155;161;165m$u$esc[38;2;159;166;169m$esc[48;2;154;159;165m$u$esc[38;2;158;165;168m$esc[48;2;158;164;171m$u$esc[38;2;158;164;169m$esc[48;2;143;150;152m$u$esc[38;2;101;106;109m$esc[48;2;75;82;82m$u$esc[38;2;133;140;141m$esc[48;2;102;109;110m$u$esc[38;2;99;105;106m$esc[48;2;124;130;133m$u$esc[38;2;110;116;119m$esc[48;2;124;131;133m$u$esc[38;2;81;89;89m$esc[48;2;78;85;85m$u$esc[38;2;94;102;102m$esc[48;2;164;169;172m$u$esc[38;2;148;154;157m$esc[48;2;165;172;176m$u$esc[38;2;108;112;113m$esc[48;2;84;91;91m$u$esc[38;2;81;87;88m$esc[48;2;94;99;101m$u$esc[38;2;87;91;92m$esc[48;2;102;108;109m$u$esc[38;2;154;159;161m$esc[48;2;102;106;108m$u$esc[38;2;117;124;126m$esc[48;2;99;103;105m$u$esc[38;2;116;122;123m$esc[48;2;124;129;130m$u$esc[38;2;67;73;74m$esc[48;2;109;113;115m$u$esc[38;2;193;194;199m$esc[48;2;164;168;169m$u$esc[38;2;201;207;211m$esc[48;2;199;204;208m$u$esc[38;2;203;204;211m$esc[48;2;200;201;207m$u$esc[38;2;187;192;194m$esc[48;2;186;190;194m$u$esc[38;2;141;148;151m$esc[48;2;140;147;150m$u$esc[38;2;175;180;185m$esc[48;2;175;180;185m$u$esc[0m $esc[0m     $esc[36m*$esc[0m $esc[1mThermodynamic Observer:$esc[0m Per-core dynamic flux
$esc[0m $esc[38;2;173;179;182m$esc[48;2;173;179;183m$u$esc[38;2;138;145;150m$esc[48;2;138;144;148m$u$esc[38;2;148;154;159m$esc[48;2;150;154;159m$u$esc[38;2;150;155;159m$esc[48;2;150;155;159m$u$esc[38;2;154;159;165m$esc[48;2;152;158;162m$u$esc[38;2;145;151;158m$esc[48;2;157;162;168m$u$esc[38;2;131;137;144m$esc[48;2;155;158;165m$u$esc[38;2;137;144;145m$esc[48;2;120;127;127m$u$esc[38;2;152;159;162m$esc[48;2;130;136;137m$u$esc[38;2;158;164;168m$esc[48;2;127;133;136m$u$esc[38;2;151;158;159m$esc[48;2;161;166;171m$u$esc[38;2;157;164;166m$esc[48;2;134;141;143m$u$esc[38;2;171;178;180m$esc[48;2;131;138;141m$u$esc[38;2;152;159;162m$esc[48;2;145;152;155m$u$esc[38;2;141;147;150m$esc[48;2;144;151;154m$u$esc[38;2;168;173;176m$esc[48;2;133;138;141m$u$esc[38;2;151;157;159m$esc[48;2;154;159;161m$u$esc[38;2;162;168;169m$esc[48;2;138;144;145m$u$esc[38;2;173;179;182m$esc[48;2;148;155;158m$u$esc[38;2;187;193;197m$esc[48;2;185;190;194m$u$esc[38;2;182;187;190m$esc[48;2;194;200;206m$u$esc[38;2;200;206;210m$esc[48;2;193;199;203m$u$esc[38;2;201;208;213m$esc[48;2;197;200;204m$u$esc[38;2;187;193;196m$esc[48;2;185;187;190m$u$esc[38;2;141;147;150m$esc[48;2;141;145;148m$u$esc[38;2;175;179;183m$esc[48;2;173;179;183m$u$esc[0m $esc[0m     $esc[35m*$esc[0m $esc[1mOpen Telemetry API:$esc[0m Local port 8765
$esc[0m $esc[38;2;171;176;182m$esc[48;2;182;186;190m$u$esc[38;2;138;145;148m$esc[48;2;138;145;148m$u$esc[38;2;150;155;159m$esc[48;2;143;147;152m$u$esc[38;2;143;150;154m$esc[48;2;136;141;145m$u$esc[38;2;150;154;159m$esc[48;2;145;151;155m$u$esc[38;2;150;157;161m$esc[48;2;148;154;159m$u$esc[38;2;151;158;162m$esc[48;2;151;157;162m$u$esc[38;2;148;155;158m$esc[48;2;154;158;164m$u$esc[38;2;154;159;164m$esc[48;2;157;162;168m$u$esc[38;2;154;161;164m$esc[48;2;159;165;169m$u$esc[38;2;157;162;166m$esc[48;2;159;166;171m$u$esc[38;2;162;169;173m$esc[48;2;161;166;172m$u$esc[38;2;159;164;166m$esc[48;2;168;171;175m$u$esc[38;2;158;165;166m$esc[48;2;166;172;178m$u$esc[38;2;166;172;176m$esc[48;2;168;175;179m$u$esc[38;2;166;173;176m$esc[48;2;171;178;180m$u$esc[38;2;173;179;183m$esc[48;2;172;180;182m$u$esc[38;2;176;182;185m$esc[48;2;180;182;187m$u$esc[38;2;178;183;187m$esc[48;2;183;185;190m$u$esc[38;2;187;193;197m$esc[48;2;182;187;192m$u$esc[38;2;190;196;200m$esc[48;2;180;186;190m$u$esc[38;2;187;193;197m$esc[48;2;178;183;189m$u$esc[38;2;193;199;203m$esc[48;2;183;190;193m$u$esc[38;2;183;186;190m$esc[48;2;176;179;182m$u$esc[38;2;140;145;147m$esc[48;2;143;145;148m$u$esc[38;2;172;178;182m$esc[48;2;183;189;190m$u$esc[38;2;12;26;21m$esc[49m$u$esc[0m     $esc[33m*$esc[0m $esc[1mZero Ring-0 Drivers:$esc[0m 0 UAC prompts, Defender-clean
$esc[0m $esc[38;2;96;106;110m$esc[49m$u$esc[38;2;152;159;162m$esc[48;2;117;120;126m$u$esc[38;2;140;145;148m$esc[48;2;150;155;161m$u$esc[38;2;136;141;145m$esc[48;2;133;138;143m$u$esc[38;2;141;148;151m$esc[48;2;138;143;148m$u$esc[38;2;144;150;155m$esc[48;2;144;150;154m$u$esc[38;2;143;148;152m$esc[48;2;144;150;154m$u$esc[38;2;147;152;157m$esc[48;2;148;154;158m$u$esc[38;2;151;157;164m$esc[48;2;152;157;162m$u$esc[38;2;154;161;165m$esc[48;2;152;158;162m$u$esc[38;2;157;162;166m$esc[48;2;155;161;165m$u$esc[38;2;162;168;172m$esc[48;2;157;162;165m$u$esc[38;2;162;169;173m$esc[48;2;161;166;171m$u$esc[38;2;165;171;175m$esc[48;2;162;166;172m$u$esc[38;2;166;172;176m$esc[48;2;164;168;172m$u$esc[38;2;169;175;179m$esc[48;2;165;171;175m$u$esc[38;2;169;176;180m$esc[48;2;166;173;178m$u$esc[38;2;173;178;183m$esc[48;2;173;178;182m$u$esc[38;2;179;182;186m$esc[48;2;178;180;185m$u$esc[38;2;178;183;187m$esc[48;2;176;182;186m$u$esc[38;2;176;182;186m$esc[48;2;178;183;187m$u$esc[38;2;178;185;187m$esc[48;2;182;185;190m$u$esc[38;2;183;189;192m$esc[48;2;187;190;192m$u$esc[38;2;171;176;176m$esc[48;2;183;187;189m$u$esc[38;2;154;158;157m$esc[48;2;116;116;120m$u$esc[38;2;99;105;106m$esc[49m$u$esc[0m $esc[0m     $esc[34m*$esc[0m $esc[1mEmbedded Streaming:$esc[0m High-frequency WebSockets
$esc[38;2;18;36;22m$esc[49m$u$esc[38;2;46;47;24m$esc[48;2;17;28;17m$u$esc[38;2;101;105;110m$esc[48;2;106;110;116m$u$esc[38;2;152;158;162m$esc[48;2;144;150;154m$u$esc[38;2;133;138;143m$esc[48;2;127;134;138m$u$esc[38;2;134;141;145m$esc[48;2;136;141;145m$u$esc[38;2;141;147;151m$esc[48;2;137;143;147m$u$esc[38;2;143;148;154m$esc[48;2;143;150;152m$u$esc[38;2;150;155;159m$esc[48;2;147;152;157m$u$esc[38;2;152;158;162m$esc[48;2;147;152;157m$u$esc[38;2;151;158;161m$esc[48;2;150;155;159m$u$esc[38;2;155;159;166m$esc[48;2;152;158;164m$u$esc[38;2;157;162;166m$esc[48;2;157;161;168m$u$esc[38;2;157;162;166m$esc[48;2;161;166;171m$u$esc[38;2;164;169;172m$esc[48;2;165;169;173m$u$esc[38;2;164;169;173m$esc[48;2;165;171;175m$u$esc[38;2;165;171;175m$esc[48;2;168;173;179m$u$esc[38;2;169;175;179m$esc[48;2;171;176;182m$u$esc[38;2;169;175;179m$esc[48;2;169;175;179m$u$esc[38;2;173;179;183m$esc[48;2;169;175;180m$u$esc[38;2;175;180;186m$esc[48;2;172;178;183m$u$esc[38;2;176;183;187m$esc[48;2;173;179;185m$u$esc[38;2;178;183;189m$esc[48;2;173;179;185m$u$esc[38;2;185;189;193m$esc[48;2;175;180;186m$u$esc[38;2;182;187;189m$esc[48;2;176;180;186m$u$esc[38;2;103;108;109m$esc[48;2;108;110;113m$u$esc[0m $esc[0m $esc[0m     $esc[32m*$esc[0m $esc[1mLean & Portable:$esc[0m Single-file self-contained binary
$esc[0m $esc[0m $esc[38;2;108;110;113m$esc[48;2;105;109;112m$u$esc[38;2;143;150;154m$esc[48;2;127;131;137m$u$esc[38;2;113;119;123m$esc[48;2;136;140;144m$u$esc[38;2;116;122;126m$esc[48;2;120;126;131m$u$esc[38;2;117;123;127m$esc[48;2;120;126;130m$u$esc[38;2;123;129;133m$esc[48;2;127;133;137m$u$esc[38;2;129;134;137m$esc[48;2;131;137;141m$u$esc[38;2;134;138;144m$esc[48;2;127;133;137m$u$esc[38;2;140;147;150m$esc[48;2;136;141;145m$u$esc[38;2;133;140;141m$esc[48;2;127;133;136m$u$esc[38;2;141;145;152m$esc[48;2;140;145;150m$u$esc[38;2;138;144;148m$esc[48;2;137;141;144m$u$esc[38;2;144;148;151m$esc[48;2;141;145;147m$u$esc[38;2;145;151;155m$esc[48;2;141;147;148m$u$esc[38;2;147;152;155m$esc[48;2;143;148;151m$u$esc[38;2;152;158;162m$esc[48;2;150;154;157m$u$esc[38;2;164;169;173m$esc[48;2;171;176;180m$u$esc[38;2;166;172;178m$esc[48;2;173;179;185m$u$esc[38;2;169;175;180m$esc[48;2;175;180;185m$u$esc[38;2;171;176;180m$esc[48;2;175;180;186m$u$esc[38;2;171;176;180m$esc[48;2;176;182;186m$u$esc[38;2;173;179;185m$esc[48;2;185;190;194m$u$esc[38;2;175;179;185m$esc[48;2;137;141;145m$u$esc[38;2;105;105;108m$esc[48;2;106;110;115m$u$esc[0m $esc[0m $esc[0m   
$esc[0m $esc[0m $esc[38;2;119;122;124m$esc[49m$u$esc[38;2;117;123;127m$esc[48;2;126;129;136m$u$esc[38;2;113;117;123m$esc[48;2;124;129;133m$u$esc[38;2;131;137;140m$esc[48;2;116;119;122m$u$esc[38;2;130;136;140m$esc[48;2;117;120;124m$u$esc[38;2;130;137;141m$esc[48;2;116;119;124m$u$esc[38;2;133;140;143m$esc[48;2;119;122;126m$u$esc[38;2;136;141;145m$esc[48;2;108;112;116m$u$esc[38;2;144;151;152m$esc[48;2;35;42;42m$u$esc[38;2;148;152;157m$esc[48;2;26;33;32m$u$esc[38;2;150;154;159m$esc[48;2;28;35;33m$u$esc[38;2;152;158;161m$esc[48;2;25;33;32m$u$esc[38;2;152;158;161m$esc[48;2;25;35;32m$u$esc[38;2;154;159;164m$esc[48;2;26;33;33m$u$esc[38;2;155;161;165m$esc[48;2;26;33;33m$u$esc[38;2;155;159;164m$esc[48;2;32;39;40m$u$esc[38;2;147;152;157m$esc[48;2;99;101;105m$u$esc[38;2;145;152;157m$esc[48;2;109;110;116m$u$esc[38;2;147;152;157m$esc[48;2;108;110;115m$u$esc[38;2;145;152;155m$esc[48;2;109;113;116m$u$esc[38;2;150;155;159m$esc[48;2;105;109;110m$u$esc[38;2;131;134;138m$esc[48;2;120;124;130m$u$esc[38;2;136;140;145m$esc[48;2;123;124;130m$u$esc[38;2;124;126;130m$esc[49m$u$esc[0m $esc[0m $esc[0m   $esc[90mVersion 1.0.0 $([char]0x2022) FoxDen Software$esc[0m
$esc[38;2;18;33;21m$esc[48;2;28;35;17m$u$esc[38;2;11;38;29m$esc[48;2;15;32;17m$u$esc[0m $esc[0m $esc[38;2;24;38;38m$esc[49m$u$esc[38;2;19;35;33m$esc[49m$u$esc[38;2;21;36;35m$esc[49m$u$esc[38;2;21;35;35m$esc[49m$u$esc[38;2;22;38;38m$esc[49m$u$esc[38;2;21;33;35m$esc[49m$u$esc[0m $esc[0m $esc[38;2;8;26;24m$esc[49m$d$esc[38;2;17;31;29m$esc[49m$d$esc[0m $esc[38;2;12;28;26m$esc[49m$d$esc[0m $esc[0m $esc[0m $esc[38;2;15;25;26m$esc[49m$u$esc[0m $esc[0m $esc[0m $esc[38;2;29;32;28m$esc[48;2;19;35;17m$u$esc[0m $esc[0m $esc[38;2;7;29;26m$esc[49m$u$esc[38;2;4;26;18m$esc[49m$u$esc[0m   
"@

$guardianBanner = $guardianBanner.Replace("$esc[49m", "$esc[48;2;0;0;0m")

# 1. Setup user-space install directory & temp staging (Zero Admin, Zero UAC)
$installDir = "$env:LOCALAPPDATA\Programs\inFaux"
if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}

$tempDir = Join-Path $env:TEMP "inFaux_Installer"
if (-not (Test-Path $tempDir)) {
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
}

$stagingExe = Join-Path $tempDir "inFaux.exe"
$targetExe = Join-Path $installDir "inFaux.exe"

Write-Host $guardianBanner
if (-not $Force) {
    $confirm = Read-Host "`n    Press [Enter] to install inFaux, or type 'C' to cancel"
    if ($confirm -match "^[cCqQ]") {
        Write-Host "`n[*] Installation cancelled." -ForegroundColor Yellow
        return
    }
}

# 3. Check for Local Binary, Local Source Code, or Download Release
$repo = "TalviFox/inFaux"
$expectedHash = $null
$releaseTag = "v1.0.0"

$candidateExe = $null
if ($Path -and (Test-Path $Path)) {
    $candidateExe = (Resolve-Path $Path).Path
}
elseif ($PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot "publish\inFaux.exe"))) {
    $candidateExe = Join-Path $PSScriptRoot "publish\inFaux.exe"
}
elseif ($PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot "inFaux.exe"))) {
    $candidateExe = Join-Path $PSScriptRoot "inFaux.exe"
}

$localProj = if ($PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot "InFox.csproj"))) { Join-Path $PSScriptRoot "InFox.csproj" } else { $null }

if ($candidateExe -and -not $Build) {
    Write-Host "[*] Pre-compiled local binary detected: $candidateExe" -ForegroundColor Cyan
    Copy-Item -Path $candidateExe -Destination $stagingExe -Force
    try {
        $verInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($candidateExe)
        if ($verInfo.ProductVersion) { $releaseTag = "v$($verInfo.ProductVersion.Trim())" } elseif ($verInfo.FileVersion) { $releaseTag = "v$($verInfo.FileVersion.Trim())" }
    }
    catch {}
    Write-Host "[+] Local binary verified and staged for installation ($releaseTag)." -ForegroundColor Green
}
elseif ($localProj -and (Test-Path $localProj)) {
    Write-Host "[*] Local source code detected. Building locally instead of downloading..." -ForegroundColor Cyan
    $publishDir = Join-Path $PSScriptRoot "publish"
    
    $dotnet = "dotnet"
    $dotnetDir = "$env:LOCALAPPDATA\Microsoft\dotnet"
    if (Test-Path "$dotnetDir\dotnet.exe") {
        $dotnet = "$dotnetDir\dotnet.exe"
    }
    
    $proc = Start-Process $dotnet -ArgumentList "publish `"$localProj`" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o `"$publishDir`"" -Wait -NoNewWindow -PassThru
    if ($proc.ExitCode -ne 0) {
        Write-Host "`n[X] Local build failed with exit code $($proc.ExitCode)." -ForegroundColor Red
        return
    }
    
    $builtExe = Join-Path $publishDir "inFaux.exe"
    if (-not (Test-Path $builtExe)) {
        Write-Host "`n[X] Local build succeeded but inFaux.exe not found at $builtExe." -ForegroundColor Red
        return
    }
    
    Copy-Item -Path $builtExe -Destination $stagingExe -Force
    Write-Host "[+] Local build compiled and verified." -ForegroundColor Green
}
else {
    $apiUrl = "https://api.github.com/repos/$repo/releases/latest"
    $headers = @{ "User-Agent" = "inFaux-Installer" }

    Write-Host "[*] Querying latest release from $repo over TLS..." -ForegroundColor Cyan

    try {
        $release = Invoke-RestMethod -Uri $apiUrl -Headers $headers -UseBasicParsing
        $releaseTag = $release.tag_name

        $sumsAsset = $release.assets | Where-Object { $_.name -match "^(SHA256SUMS|inFaux.*)\.(txt|sha256)$" -or $_.name -eq "inFaux.exe.sha256" } | Select-Object -First 1
        if ($sumsAsset) {
            $checksumText = Invoke-RestMethod -Uri $sumsAsset.browser_download_url -Headers $headers -UseBasicParsing
            if ($checksumText -match "([a-fA-F0-9]{64})\s+.*inFaux\.exe") {
                $expectedHash = $matches[1].ToLowerInvariant()
            }
            elseif ($checksumText -match "\b([a-fA-F0-9]{64})\b") {
                $expectedHash = $matches[1].ToLowerInvariant()
            }
        }

        if (-not $expectedHash -and $release.body -match "\b([a-fA-F0-9]{64})\b") {
            $expectedHash = $matches[1].ToLowerInvariant()
        }
    }
    catch {
        Write-Host "[!] Could not query release API metadata. Proceeding with direct binary download..." -ForegroundColor Yellow
    }

    $downloadUrl = "https://github.com/$repo/releases/latest/download/inFaux.exe"
    Write-Host "[*] Downloading inFaux ($releaseTag)..." -ForegroundColor Cyan
    try {
        Invoke-WebRequest -Uri $downloadUrl -OutFile $stagingExe -UseBasicParsing
    }
    catch {
        Write-Host "`n[X] Failed to download inFaux.exe from GitHub Releases ($downloadUrl)." -ForegroundColor Red
        Write-Host "    Please ensure a Release containing 'inFaux.exe' exists at:" -ForegroundColor Yellow
        Write-Host "    https://github.com/$repo/releases" -ForegroundColor White
        return
    }

    # Verify SHA-256
    Write-Host "[*] Verifying cryptographic SHA-256 integrity..." -ForegroundColor Cyan
    $actualHash = (Get-FileHash -Path $stagingExe -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "    Downloaded SHA-256: $actualHash" -ForegroundColor White

    if ($expectedHash) {
        if ($actualHash -ne $expectedHash) {
            Write-Host "`n[X] CRITICAL: SHA-256 CHECKSUM VERIFICATION FAILED!" -ForegroundColor Red
            Write-Host "    Expected: $expectedHash" -ForegroundColor Yellow
            Write-Host "    Actual:   $actualHash" -ForegroundColor Yellow
            Remove-Item -Path $stagingExe -Force -ErrorAction SilentlyContinue
            return
        }
        Write-Host "[+] SHA-256 integrity verified successfully!" -ForegroundColor Green
    }
}

# 4. Stop running instance if updating
$running = Get-Process -Name "inFaux", "inFox" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "[*] Stopping running inFaux process..." -ForegroundColor Cyan
    $running | Stop-Process -Force
    Start-Sleep -Seconds 1
}

# 5. Move verified binary into place
Copy-Item -Path $stagingExe -Destination $targetExe -Force
Remove-Item -Path $stagingExe -Force -ErrorAction SilentlyContinue
Write-Host "[+] Installed to: $targetExe" -ForegroundColor Green

# 6. Deploy companion scripts (uninstaller & verifier)
$uninstallerTarget = Join-Path $installDir "uninstall.ps1"
$verifierTarget = Join-Path $installDir "verify.ps1"

$localUninstall = if ($PSScriptRoot) { Join-Path $PSScriptRoot "uninstall.ps1" } else { $null }
$localVerifier = if ($PSScriptRoot) { Join-Path $PSScriptRoot "verify.ps1" } else { $null }

if ($localUninstall -and (Test-Path $localUninstall)) {
    Copy-Item -Path $localUninstall -Destination $uninstallerTarget -Force
    Write-Host "[+] Local uninstaller script deployed: $uninstallerTarget" -ForegroundColor Green
}
else {
    try {
        $uninstallerUrl = "https://raw.githubusercontent.com/$repo/main/uninstall.ps1"
        Invoke-WebRequest -Uri $uninstallerUrl -OutFile $uninstallerTarget -UseBasicParsing
        Write-Host "[+] Uninstaller script deployed: $uninstallerTarget" -ForegroundColor Green
    }
    catch {}
}

if ($localVerifier -and (Test-Path $localVerifier)) {
    Copy-Item -Path $localVerifier -Destination $verifierTarget -Force
    Write-Host "[+] Local integrity auditor deployed: $verifierTarget" -ForegroundColor Green
}
else {
    try {
        $verifierUrl = "https://raw.githubusercontent.com/$repo/main/verify.ps1"
        Invoke-WebRequest -Uri $verifierUrl -OutFile $verifierTarget -UseBasicParsing
        Write-Host "[+] Integrity auditor script deployed: $verifierTarget" -ForegroundColor Green
    }
    catch {}
}

# 7. Register in Windows "Installed Apps" (Add or Remove Programs - User Level)
try {
    $regKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\inFaux"
    if (-not (Test-Path $regKey)) {
        New-Item -Path $regKey -Force | Out-Null
    }
    Set-ItemProperty -Path $regKey -Name "DisplayName" -Value "inFaux"
    Set-ItemProperty -Path $regKey -Name "DisplayVersion" -Value $releaseTag.TrimStart('v', 'V')
    Set-ItemProperty -Path $regKey -Name "Publisher" -Value "FoxDen Software"
    Set-ItemProperty -Path $regKey -Name "DisplayIcon" -Value "$targetExe,0"
    Set-ItemProperty -Path $regKey -Name "InstallLocation" -Value $installDir
    Set-ItemProperty -Path $regKey -Name "UninstallString" -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstallerTarget`""
    Set-ItemProperty -Path $regKey -Name "URLInfoAbout" -Value "https://github.com/TalviFox/inFaux"
    Write-Host "[+] Registered in Windows Installed Apps." -ForegroundColor Green
}
catch {
    Write-Host "[!] Could not register in Windows Uninstall registry: $_" -ForegroundColor Yellow
}

# 8. Create Start Menu Shortcut (User Level)
try {
    $wsh = New-Object -ComObject WScript.Shell
    $startMenuDir = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs"
    $shortcut = $wsh.CreateShortcut((Join-Path $startMenuDir "inFaux.lnk"))
    $shortcut.TargetPath = $targetExe
    $shortcut.WorkingDirectory = $installDir
    $shortcut.IconLocation = "$targetExe,0"
    $shortcut.Description = "inFaux - Modern Native Hardware Monitor & Open Telemetry Server"
    $shortcut.Save()
    Write-Host "[+] Start Menu shortcut created." -ForegroundColor Green
}
catch {
    Write-Host "[!] Could not create Start Menu shortcut: $_" -ForegroundColor Yellow
}

# 9. Complete & Launch
$checkEmoji = [char]::ConvertFromUtf32(0x2705)
Write-Host @"

  ======================================================
     $checkEmoji inFaux has been installed successfully!
  ======================================================
  Install Path: $targetExe
  Telemetry API: http://localhost:8765/api/v1/summary
  ======================================================
"@ -ForegroundColor Green

Write-Host "[*] Launching inFaux..." -ForegroundColor Cyan
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($isAdmin) {
    # If the installer was run inside an elevated shell, de-elevate through explorer so inFaux runs as standard user
    Start-Process -FilePath "explorer.exe" -ArgumentList "`"$targetExe`""
}
else {
    Start-Process -FilePath $targetExe
}

if (-not $Force) {
    Read-Host "`n    Press [Enter] to exit installer"
}
