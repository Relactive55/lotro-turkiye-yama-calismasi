[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$UpdaterExe,
    [Parameter(Mandatory = $true)][string]$CertificatePath,
    [string]$CertificatePassword = $env:LOTRO_CODE_SIGN_CERT_PASSWORD,
    [string]$TimestampServer = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$exe = (Get-Item -LiteralPath $UpdaterExe).FullName
if ([string]::IsNullOrWhiteSpace($CertificatePassword)) {
    throw 'Code-signing certificate password must come from a protected environment secret.'
}
$securePassword = ConvertTo-SecureString $CertificatePassword -AsPlainText -Force
$certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
    (Get-Item -LiteralPath $CertificatePath).FullName,
    $securePassword,
    [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::MachineKeySet)
if (!$certificate.HasPrivateKey) { throw 'The code-signing certificate has no private key.' }
$result = Set-AuthenticodeSignature -FilePath $exe -Certificate $certificate -TimestampServer $TimestampServer
if ($result.Status -ne 'Valid') { throw "Authenticode signing failed: $($result.Status) $($result.StatusMessage)" }
$verified = Get-AuthenticodeSignature -FilePath $exe
if ($verified.Status -ne 'Valid') { throw "Authenticode verification failed: $($verified.Status) $($verified.StatusMessage)" }
"CODE_SIGNED|path=$exe|thumbprint=$($verified.SignerCertificate.Thumbprint)"
