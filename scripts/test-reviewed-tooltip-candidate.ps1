param([Parameter(Mandatory)][string]$CandidateDat)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
[void][Reflection.Assembly]::LoadFrom((Join-Path $repo 'developer-tool/automation/SemanticPatchGenerator/bin/Release/net472/SemanticPatchGenerator.exe'))
$spec=Get-Content (Join-Path $repo 'developer-tool/automation/reviewed/tooltip-2026-09-08.spec.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$wanted=@{}
foreach($row in $spec.rows){$wanted.Add($row[0],$row[2])}
foreach($group in $spec.groups){foreach($did in $group.dids){$wanted.Add($did+':0:-1:0',$group.target)}}
$dat=[LotroTrGemini.TurbineDat]::new()
try{
 $dat.Open((Get-Item -LiteralPath $CandidateDat).FullName,$false)
 foreach($group in ($wanted.Keys | Group-Object {$_.Substring(0,8)})){
  $did=[Convert]::ToInt32($group.Name,16);$entry=[LotroTrGemini.DatEntry]::new()
  if(!$dat.TryGetEntry($did,[ref]$entry)){throw 'Missing reviewed DID'}
  $rows=@{}
  foreach($row in [LotroTrGemini.LocBin]::Parse($dat.ReadRaw($entry),$did).GetRows($did)){$rows.Add($row.Key,$row.Original)}
  foreach($key in $group.Group){if($rows[$key] -cne $wanted[$key]){throw "Tooltip target mismatch: $key"}}
 }
 foreach($did in @(0x250001AF,0x250001BB)){
  $entry=[LotroTrGemini.DatEntry]::new()
  if(!$dat.TryGetEntry($did,[ref]$entry)){throw 'Missing shared tooltip table'}
  $raw=$dat.ReadRaw($entry)
  $verified=[LotroTrGemini.KnownUiFixes]::ApplyTranslatedPayload($did,$raw)
  if([Convert]::ToBase64String($raw) -cne [Convert]::ToBase64String($verified)){throw 'Shared tooltip targets are not fully applied'}
 }
 $gondolin=[LotroTrGemini.DatEntry]::new()
 if(!$dat.TryGetEntry(0x2503B6C1,[ref]$gondolin)){throw 'Missing title table'}
 $gondolinRaw=$dat.ReadRaw($gondolin)
 $gondolinFixed=[LotroTrGemini.KnownUiFixes]::ApplyTranslatedPayload(0x2503B6C1,$gondolinRaw)
 if([Convert]::ToBase64String($gondolinRaw) -cne [Convert]::ToBase64String($gondolinFixed)){throw 'Title suffix target is not fully applied'}
}finally{$dat.Dispose()}
"TOOLTIP_CANDIDATE_PASS|reviewed=$($wanted.Count)|shared-tables=2|hidden-native-records=5|title-tables=1|read-only=true"
