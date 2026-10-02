<#
.SYNOPSIS
  在没有打开 Unity 的情况下，验证客户端工程能不能编译，并跑核心算法自检。

.DESCRIPTION
  用 Unity 自带的 Roslyn 编译器 + UnityEngine/UnityEditor 程序集，
  按 Unity 实际的程序集划分编译三遍：
    A) Dogfight.Core        —— 纯 C#，不引用任何 UnityEngine（工程里由 asmdef 的
                               noEngineReferences 强制约束，这里用「不传引擎引用」等价模拟）
    B) Assembly-CSharp      —— Gameplay / View / Config / Tests，引用 A + UnityEngine
    C) Assembly-CSharp-Editor —— Editor 工具，引用 B + UnityEngine + UnityEditor

  这样不需要启动编辑器（编辑器需要图形环境与许可证），也能保证提交的脚本是能编译的。
  最后再把 Core + 自检用 mono 跑一遍，验证算法层不只是「能编译」而是「真的能跑对」。

.EXAMPLE
  pwsh tools/unity-verify/verify-all.ps1
  pwsh tools/unity-verify/verify-all.ps1 -EditorRoot "C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1"
#>
param(
    [string]$EditorRoot = "",
    [switch]$SkipRun
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------- 定位编辑器
function Find-EditorRoot {
    if ($EditorRoot -and (Test-Path $EditorRoot)) { return $EditorRoot }

    $candidates = @()
    foreach ($base in @("C:\Program Files", "C:\Program Files (x86)", "D:\", "E:\")) {
        if (-not (Test-Path $base)) { continue }
        $candidates += Get-ChildItem $base -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^(Unity|Tuanjie)' } |
            ForEach-Object { Join-Path $_.FullName "Editor" }
    }
    foreach ($c in $candidates) {
        if (Test-Path (Join-Path $c "Data\Managed\UnityEngine.dll")) { return $c }
    }
    return $null
}

$editor = Find-EditorRoot
if (-not $editor) {
    Write-Host "  [x] 没找到 Unity / 团结引擎的 Editor 目录。" -ForegroundColor Red
    Write-Host "      用 -EditorRoot 显式指定，例如："
    Write-Host '      pwsh tools/unity-verify/verify-all.ps1 -EditorRoot "C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1"'
    exit 2
}

$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$proj = Join-Path $repo "src\client\DogfightThreatAI"
$scripts = Join-Path $proj "Assets\Scripts"
$outDir = Join-Path $repo "Temp\verify"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$csc = Join-Path $editor "Data\DotNetSdkRoslyn\csc.dll"
$mono = Join-Path $editor "Data\MonoBleedingEdge\bin\mono.exe"
$managed = Join-Path $editor "Data\Managed"
$netstandard = Join-Path $editor "Data\NetStandard\ref\2.1.0\netstandard.dll"

if (-not (Test-Path $csc)) { Write-Host "  [x] 找不到 csc.dll：$csc" -ForegroundColor Red; exit 2 }

$engineRefs = @((Join-Path $managed "UnityEngine.dll")) +
              @(Get-ChildItem (Join-Path $managed "UnityEngine") -Filter *.dll -ErrorAction SilentlyContinue |
                ForEach-Object { $_.FullName })

Write-Host ""
Write-Host "  空战 · 客户端编译验证" -ForegroundColor Cyan
Write-Host "  编辑器 : $editor"
Write-Host "  工程   : $proj"
Write-Host ""

function Invoke-Csc {
    param([string]$Label, [string[]]$Sources, [string[]]$Refs, [string]$Target, [string]$OutFile)

    if ($Sources.Count -eq 0) { Write-Host "  [!] $Label 没有源文件，跳过"; return $false }

    $args = @($csc, "-nologo", "-noconfig", "-target:$Target", "-langversion:9.0", "-nostdlib",
              "-out:$OutFile")
    $args += $Refs | ForEach-Object { "-r:`"$_`"" }
    $args += $Sources

    $output = & dotnet @args 2>&1
    $ok = ($LASTEXITCODE -eq 0) -and (Test-Path $OutFile)
    if ($ok) {
        Write-Host ("  [OK]   {0,-26} {1} 个文件" -f $Label, $Sources.Count) -ForegroundColor Green
    } else {
        Write-Host ("  [FAIL] {0}" -f $Label) -ForegroundColor Red
        $output | Select-Object -First 40 | ForEach-Object { Write-Host "         $_" -ForegroundColor DarkYellow }
        if ($output.Count -gt 40) { Write-Host ("         ... 还有 {0} 行" -f ($output.Count - 40)) }
    }
    return $ok
}

$allOk = $true

# ---------------------------------------------------- A) Core（纯 C#，无引擎引用）
$coreFiles = @(Get-ChildItem (Join-Path $scripts "Core") -Filter *.cs -ErrorAction SilentlyContinue |
               ForEach-Object { $_.FullName })
$coreDll = Join-Path $outDir "Dogfight.Core.dll"
$allOk = (Invoke-Csc -Label "Dogfight.Core（纯 C#）" -Sources $coreFiles `
    -Refs @($netstandard) -Target "library" -OutFile $coreDll) -and $allOk

# ------------------------------------------- B) Assembly-CSharp（运行时 + 自检）
$runtimeFiles = @()
foreach ($sub in @("Gameplay", "View", "Config", "Tests")) {
    $dir = Join-Path $scripts $sub
    if (Test-Path $dir) {
        $runtimeFiles += @(Get-ChildItem $dir -Filter *.cs -Recurse | ForEach-Object { $_.FullName })
    }
}
$runtimeDll = Join-Path $outDir "Assembly-CSharp.dll"
$allOk = (Invoke-Csc -Label "Assembly-CSharp（运行时）" -Sources $runtimeFiles `
    -Refs (@($netstandard, $coreDll) + $engineRefs) -Target "library" -OutFile $runtimeDll) -and $allOk

# ------------------------------------ C) Assembly-CSharp-Editor（编辑器工具）
$editorFiles = @(Get-ChildItem (Join-Path $scripts "Editor") -Filter *.cs -Recurse -ErrorAction SilentlyContinue |
                 ForEach-Object { $_.FullName })
$editorDll = Join-Path $outDir "Assembly-CSharp-Editor.dll"
$allOk = (Invoke-Csc -Label "Assembly-CSharp-Editor" -Sources $editorFiles `
    -Refs (@($netstandard, $coreDll, $runtimeDll, (Join-Path $managed "UnityEditor.dll")) + $engineRefs) `
    -Target "library" -OutFile $editorDll) -and $allOk

Write-Host ""

# ------------------------------------------------- D) 跑核心算法自检（mono）
if (-not $SkipRun -and $allOk) {
    Write-Host "  运行核心算法自检（Unity 自带 mono）" -ForegroundColor Cyan
    $testFiles = $coreFiles + @(Join-Path $scripts "Tests\CoreSelfTest.cs") +
                 @(Join-Path $PSScriptRoot "RunTests.cs")
    $exe = Join-Path $outDir "SelfTest.exe"
    $ok = Invoke-Csc -Label "CoreSelfTest（可执行）" -Sources $testFiles `
        -Refs @($netstandard) -Target "exe" -OutFile $exe
    if ($ok) {
        Write-Host ""
        & $mono $exe
        Write-Host ""
        if ($LASTEXITCODE -ne 0) { $allOk = $false }
    } else {
        $allOk = $false
    }
}

Write-Host "  ============================================"
if ($allOk) { Write-Host "  全部通过" -ForegroundColor Green }
else { Write-Host "  存在失败项，请看上面输出" -ForegroundColor Red }
Write-Host "  ============================================"
Write-Host ""

exit ($(if ($allOk) { 0 } else { 1 }))
