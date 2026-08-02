$xmlToAdd = @"
  <data name="Tetris_Title" xml:space="preserve"><value>Tetris</value></data>
</root>
"@

Get-ChildItem -Path "D:\crate\src\CRATE.Core\Resources\*.resx" | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $newContent = $content -replace "</root>\s*`$", $xmlToAdd
    Set-Content -Path $_.FullName -Value $newContent -Encoding UTF8
}
