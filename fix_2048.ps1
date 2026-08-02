$xmlToAdd = @"
  <data name="Game2048_Title" xml:space="preserve"><value>2048</value></data>
</root>
"@

Get-ChildItem -Path "D:\crate\src\CRATE.Core\Resources\*.resx" | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $newContent = $content -replace "</root>\s*`$", $xmlToAdd
    Set-Content -Path $_.FullName -Value $newContent -Encoding UTF8
}
