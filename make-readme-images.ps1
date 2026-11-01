# Rebuilds the README pictures in docs\images from the sample shop the screen tests use (tests\PrintInvoicer.UiTests\SampleShop.cs).
# Runs one screen test offscreen, so no window appears. Works in Windows PowerShell 5.1; PrintInvoicer can stay open.
param(
    [string]$OutDir = (Join-Path $PSScriptRoot 'docs\images')
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
# The test runs from another folder, so it needs a full path.
$OutDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutDir)
# The pictures ReadmeImageTests writes; this run must rewrite every one.
$images = @('main-jobs.png', 'job-editor.png', 'invoice-page-1.png')
$started = Get-Date

$env:PRINTINVOICER_README_IMAGES = $OutDir
try {
    # AUDIT: opens a program. dotnet test, filtered to ReadmeImageTests; that test writes the pictures.
    & dotnet test (Join-Path $root 'tests\PrintInvoicer.UiTests') --nologo --filter 'Category=ReadmeImages'
    if ($LASTEXITCODE -ne 0) { throw 'The README image test failed; the images were not updated.' }
}
finally {
    Remove-Item Env:\PRINTINVOICER_README_IMAGES -ErrorAction SilentlyContinue
}

# dotnet test exits 0 when the filter matches no test, so check that every picture was really rewritten.
foreach ($name in $images) {
    $file = Join-Path $OutDir $name
    # AUDIT: file read. Only whether each picture exists and when it was last written.
    if (-not (Test-Path $file) -or (Get-Item $file).LastWriteTime -lt $started) {
        throw "$name was not rebuilt. Check that the test filter still matches ReadmeImageTests."
    }
}

Write-Host "Rebuilt the README images in $OutDir. Run 'git status docs/images' to see which changed."
