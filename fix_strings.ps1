$xmlToAdd = @"
  <data name="Weather_Title" xml:space="preserve"><value>Weather</value></data>
  <data name="Notepad_Title" xml:space="preserve"><value>Notepad</value></data>
  <data name="Clicker_Title" xml:space="preserve"><value>Clicker</value></data>
  <data name="Tool_PortLookup_Name" xml:space="preserve"><value>Port Lookup</value></data>
  <data name="Tool_PortLookup_Desc" xml:space="preserve"><value>Search for default port assignments.</value></data>
  <data name="Tool_PortLookup_Aliases" xml:space="preserve"><value>port, network</value></data>
  <data name="Tool_MimeType_Name" xml:space="preserve"><value>MIME Types</value></data>
  <data name="Tool_MimeType_Desc" xml:space="preserve"><value>Look up MIME types by extension.</value></data>
  <data name="Tool_MimeType_Aliases" xml:space="preserve"><value>mime, content-type, extension</value></data>
  <data name="Tool_DnsLookup_Name" xml:space="preserve"><value>DNS Lookup</value></data>
  <data name="Tool_DnsLookup_Desc" xml:space="preserve"><value>Resolve domains to IP addresses.</value></data>
  <data name="Tool_DnsLookup_Aliases" xml:space="preserve"><value>dns, ip, resolve</value></data>
  <data name="Tool_CurlToCode_Name" xml:space="preserve"><value>Curl to Code</value></data>
  <data name="Tool_CurlToCode_Desc" xml:space="preserve"><value>Convert a curl command into C# code.</value></data>
  <data name="Tool_CurlToCode_Aliases" xml:space="preserve"><value>curl, csharp, code</value></data>
  <data name="Tool_EnvVars_Name" xml:space="preserve"><value>Environment Variables</value></data>
  <data name="Tool_EnvVars_Desc" xml:space="preserve"><value>List all environment variables.</value></data>
  <data name="Tool_EnvVars_Aliases" xml:space="preserve"><value>env, variables</value></data>
  <data name="Tool_Inspector_Name" xml:space="preserve"><value>Text Inspector</value></data>
  <data name="Tool_Inspector_Desc" xml:space="preserve"><value>Analyze text characters, bytes, and unicode.</value></data>
  <data name="Tool_Inspector_Aliases" xml:space="preserve"><value>inspect, chars, bytes, hex</value></data>
  <data name="Tool_CaseConverter_Name" xml:space="preserve"><value>Case Converter</value></data>
  <data name="Tool_CaseConverter_Desc" xml:space="preserve"><value>Convert text case (camel, snake, pascal, etc).</value></data>
  <data name="Tool_CaseConverter_Aliases" xml:space="preserve"><value>case, camel, snake, kebab</value></data>
</root>
"@

Get-ChildItem -Path "D:\crate\src\CRATE.Core\Resources\*.resx" | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $newContent = $content -replace "</root>\s*`$", $xmlToAdd
    Set-Content -Path $_.FullName -Value $newContent -Encoding UTF8
}
