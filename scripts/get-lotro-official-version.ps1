[CmdletBinding()]
param(
    [string]$DatacenterResponsePath,
    [string]$LauncherConfigPath,
    [switch]$AsJson
)

$ErrorActionPreference = 'Stop'
$glsUri = [Uri]'https://gls.lotro.com/GLS.DataCenterServer/Service.asmx'

function ConvertFrom-SafeXml {
    param([Parameter(Mandatory = $true)][string]$Text)
    $settings = New-Object System.Xml.XmlReaderSettings
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create((New-Object IO.StringReader($Text)), $settings)
    try {
        $document = New-Object System.Xml.XmlDocument
        $document.XmlResolver = $null
        $document.Load($reader)
        return $document
    }
    finally {
        $reader.Dispose()
    }
}

if ($DatacenterResponsePath) {
    $datacenterText = Get-Content -LiteralPath $DatacenterResponsePath -Raw -Encoding UTF8
}
else {
    $soapBody = '<?xml version="1.0" encoding="utf-8"?><soap:Envelope xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body><GetDatacenters xmlns="http://www.turbine.com/SE/GLS"><game>LOTRO</game></GetDatacenters></soap:Body></soap:Envelope>'
    $response = Invoke-WebRequest -Uri $glsUri -Method Post -UseBasicParsing `
        -ContentType 'text/xml; charset=utf-8' `
        -Headers @{ SOAPAction = '"http://www.turbine.com/SE/GLS/GetDatacenters"' } `
        -Body $soapBody
    $datacenterText = $response.Content
}

$datacenterXml = ConvertFrom-SafeXml -Text $datacenterText
$configNodes = @($datacenterXml.SelectNodes('//*[local-name()="LauncherConfigurationServer"]'))
if ($configNodes.Count -ne 1 -or [string]::IsNullOrWhiteSpace($configNodes[0].InnerText)) {
    throw "Resmi LOTRO launcher yapılandırma adresi tekil bulunamadı (bulunan=$($configNodes.Count))."
}

$configUri = $null
if (-not [Uri]::TryCreate($configNodes[0].InnerText.Trim(), [UriKind]::Absolute, [ref]$configUri)) {
    throw 'Resmi LOTRO launcher yapılandırma adresi geçersiz.'
}
if ($configUri.Host -ne 'gls.lotro.com') {
    throw "Beklenmeyen LOTRO yapılandırma sunucusu reddedildi: $($configUri.Host)"
}
$builder = New-Object UriBuilder($configUri)
$builder.Scheme = 'https'
$builder.Port = -1
$configUri = $builder.Uri

if ($LauncherConfigPath) {
    $launcherText = Get-Content -LiteralPath $LauncherConfigPath -Raw -Encoding UTF8
}
else {
    $launcherText = (Invoke-WebRequest -Uri $configUri -UseBasicParsing).Content
}

$launcherXml = ConvertFrom-SafeXml -Text $launcherText
$versionNodes = @($launcherXml.SelectNodes('//*[local-name()="add" and @key="Game.Version"]'))
if ($versionNodes.Count -ne 1) {
    throw "LOTRO Game.Version tekil bulunamadı (bulunan=$($versionNodes.Count))."
}
$version = $versionNodes[0].GetAttribute('value').Trim()
if ($version -notmatch '^[0-9]+(?:\.[0-9]+){3}$') {
    throw "LOTRO Game.Version biçimi geçersiz: $version"
}

$result = [ordered]@{
    game = 'LOTRO'
    channel = 'official-ssg-public'
    version = $version
    launcher_config_url = $configUri.AbsoluteUri
}

if ($AsJson) { $result | ConvertTo-Json -Compress } else { $result.version }
