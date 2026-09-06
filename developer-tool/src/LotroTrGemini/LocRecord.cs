using System.Collections.Generic;

namespace LotroTrGemini;

internal sealed class LocRecord
{
	public long Hash;

	public List<string> Main = new List<string>();

	public List<int> Unk = new List<int>();

	public List<List<string>> Subs = new List<List<string>>();
}
