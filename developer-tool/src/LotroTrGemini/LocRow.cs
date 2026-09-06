namespace LotroTrGemini;

public sealed class LocRow
{
	public int Did;

	public int RecordIndex;

	public int GroupIndex;

	public int IndexInGroup;

	public string Original;

	public string Translation;

	public string Key => Did.ToString("X8") + ":" + RecordIndex + ":" + GroupIndex + ":" + IndexInGroup;
}
