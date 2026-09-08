param([Parameter(Mandatory)][string]$SourceDat,[Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
[void][Reflection.Assembly]::LoadFrom((Join-Path $repo 'developer-tool/automation/SemanticPatchGenerator/bin/Release/net472/SemanticPatchGenerator.exe'))
$spec=Get-Content (Join-Path $repo 'developer-tool/automation/reviewed/tooltip-2026-09-08.spec.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$wanted=@{}
foreach($row in $spec.rows){$wanted.Add($row[0],@($row[1],$row[2]))}
foreach($group in $spec.groups){foreach($did in $group.dids){$wanted.Add($did+':0:-1:0',@($group.source,$group.target))}}
$dat=[LotroTrGemini.TurbineDat]::new()
$lines=[Collections.Generic.List[string]]::new()
try{
 $dat.Open((Get-Item -LiteralPath $SourceDat).FullName,$false)
 foreach($group in ($wanted.Keys | Group-Object {$_.Substring(0,8)})){
  $did=[Convert]::ToInt32($group.Name,16);$entry=[LotroTrGemini.DatEntry]::new()
  if(!$dat.TryGetEntry($did,[ref]$entry)){throw 'Missing reviewed DID'}
  $pos=[long]0
  $records=@{}
  foreach($record in [LotroTrGemini.LocBin]::Parse($dat.ReadRaw($entry),$did).GetCatalogRecords($did,[ref]$pos)){$records.Add($record.Key,$record)}
  foreach($key in $group.Group){
   $record=$records[$key];$pair=$wanted[$key]
   if(!$record -or $record.Source -cne $pair[0]){throw "Reviewed source drift: $key"}
   if(![LotroTrGemini.ProtectedFormat]::HasSameProtectedTokens($record.Source,$pair[1])){throw "Protected format drift: $key"}
   $lines.Add(([ordered]@{dat_key=$key;action='translate';source_digest=$record.SourceDigest;token_signature=$record.TokenSignature;target=$pair[1]} | ConvertTo-Json -Compress))
  }
 }
}finally{$dat.Dispose()}
$stream=[IO.File]::Open($OutputPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
$writer=[IO.StreamWriter]::new($stream,[Text.UTF8Encoding]::new($false))
try{foreach($line in ($lines | Sort-Object)){$writer.WriteLine($line)}}finally{$writer.Dispose()}
"REVIEWED_DECISIONS_READY|count=$($lines.Count)|source-read-only=true"
