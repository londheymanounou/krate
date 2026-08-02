$xmlToAdd = @"
  <data name="StripMetadata_Title" xml:space="preserve"><value>Strip Image Metadata</value></data>
  <data name="ImageMetadata_Success" xml:space="preserve"><value>Successfully removed metadata. Saved to: {0}</value></data>
  <data name="Error_StripMetadataUsage" xml:space="preserve"><value>Usage: crate stripmetadata "input.jpg" | "output.jpg"</value></data>
</root>
"@

Get-ChildItem -Path "D:\crate\src\CRATE.Core\Resources\*.resx" | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $newContent = $content -replace "</root>\s*`$", $xmlToAdd
    Set-Content -Path $_.FullName -Value $newContent -Encoding UTF8
}
