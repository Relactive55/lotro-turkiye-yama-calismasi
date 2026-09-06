using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace LotroTrGemini;

internal static class GpuTranslator
{
	internal sealed class Result
	{
		public int Index;
		public string Translation;
		public string QualityProblem;
	}

	private static string PythonIn(string root) => string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, "venv", "Scripts", "python.exe");

	private static string ReadyIn(string root) => string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, "lotro_gpu_ready.txt");

	private static bool IsReady(string root) => File.Exists(PythonIn(root)) && File.Exists(ReadyIn(root));

	public static string FindRuntime()
	{
		List<string> candidates = new List<string>();
		Action<string> add = delegate(string path)
		{
			if (string.IsNullOrWhiteSpace(path)) return;
			path = path.Trim();
			foreach (string current in candidates)
			{
				if (string.Equals(current, path, StringComparison.OrdinalIgnoreCase)) return;
			}
			candidates.Add(path);
		};

		add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gpu_runtime"));
		add(Path.Combine(Program.AppDir, "gpu_runtime"));

		foreach (string file in new string[1]
		{
			Path.Combine(Program.AppDir, "gpu_runtime_path.txt")
		})
		{
			try
			{
				if (File.Exists(file)) add(File.ReadAllText(file, Encoding.UTF8));
			}
			catch
			{
			}
		}

		foreach (string candidate in candidates)
		{
			if (IsReady(candidate)) return candidate;
		}
		return null;
	}

	private static string DefaultRuntime()
	{
		return Path.Combine(Program.AppDir, "gpu_runtime");
	}

	public static async Task<string> InstallRuntimeAsync(Action<string> status, CancellationToken ct)
	{
		string script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lotro_gpu_setup.ps1");
		if (!File.Exists(script)) throw new FileNotFoundException("GPU kurulum dosyası bulunamadı.", script);
		string target = DefaultRuntime();
		ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File " + Quote(script) + " -Target " + Quote(target))
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		using (Process process = Process.Start(psi))
		{
			if (process == null) throw new InvalidOperationException("GPU kurulum işlemi başlatılamadı.");
			process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (!string.IsNullOrWhiteSpace(e.Data)) status?.Invoke(e.Data); };
			process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (!string.IsNullOrWhiteSpace(e.Data)) status?.Invoke(e.Data); };
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			using (ct.Register(delegate { try { if (!process.HasExited) process.Kill(); } catch { } }))
			{
				await Task.Run(delegate { process.WaitForExit(); }).ConfigureAwait(false);
			}
			ct.ThrowIfCancellationRequested();
			if (process.ExitCode != 0) throw new Exception("GPU bileşenleri kurulamadı (kod " + process.ExitCode + ").");
		}
		if (!IsReady(target)) throw new Exception("GPU ortamı kurulumdan sonra doğrulanamadı.");
		RememberRuntime(target);
		return target;
	}

	private static void RememberRuntime(string target)
	{
		foreach (string file in new string[1]
		{
			Path.Combine(Program.AppDir, "gpu_runtime_path.txt")
		})
		{
			try { File.WriteAllText(file, target, new UTF8Encoding(false)); } catch { }
		}
	}

	public static async Task<List<Result>> TranslateAsync(IList<string> texts, IList<string> protectedNames, string runtime, Action<int, int, string> progress, CancellationToken ct)
	{
		string python = PythonIn(runtime);
		string worker = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lotro_gpu_translate.py");
		if (!IsReady(runtime)) throw new FileNotFoundException("GPU Python ortamı eksik veya doğrulanmamış.", python);
		if (!File.Exists(worker)) throw new FileNotFoundException("GPU çeviri işçisi bulunamadı.", worker);

		string job = Path.Combine(Program.AppDir, "gpu_job_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(job);
		string input = Path.Combine(job, "input.jsonl");
		string output = Path.Combine(job, "output.jsonl");
		string context = Path.Combine(job, "context.json");
		string cache = Path.Combine(runtime, "models");
		string liveLog = Path.Combine(Program.AppDir, "gpu_quality_log.tsv");
		JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

		try
		{
			using (StreamWriter writer = new StreamWriter(input, false, new UTF8Encoding(false)))
			{
				for (int i = 0; i < texts.Count; i++)
				{
					Dictionary<string, object> row = new Dictionary<string, object>
					{
						{ "key", i.ToString() },
						{ "english", texts[i] ?? "" },
						{ "category", Classify(texts[i] ?? "") }
					};
					writer.WriteLine(json.Serialize(row));
				}
			}

			Dictionary<string, object> glossary = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
			foreach (KeyValuePair<string, string> item in LotroGpuContext.Glossary) glossary[item.Key] = item.Value;
			Dictionary<string, object> contextData = new Dictionary<string, object>
			{
				{ "version", "lotro-opus-qwen-quality-v1" },
				{ "language", "tr-TR" },
				{ "protected_names", protectedNames ?? new string[0] },
				{ "glossary", glossary },
				{ "policy", "LOTRO özel adları, sayılar ve biçim kodları aynen korunur; doğal, tutarlı ve eksiksiz Türkçe kullanılır." }
			};
			File.WriteAllText(context, json.Serialize(contextData), new UTF8Encoding(false));

			ProcessStartInfo psi = new ProcessStartInfo(python,
				Quote(worker) + " --input " + Quote(input) + " --output " + Quote(output) + " --cache " + Quote(cache) +
				" --context " + Quote(context) + " --live-log " + Quote(liveLog) + " --batch 32 --qwen-model 8b-q4")
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				StandardOutputEncoding = Encoding.UTF8,
				StandardErrorEncoding = Encoding.UTF8,
				WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
			};
			psi.EnvironmentVariables["PYTHONUTF8"] = "1";
			psi.EnvironmentVariables["HF_HUB_DISABLE_PROGRESS_BARS"] = "1";
			StringBuilder errors = new StringBuilder();
			using (Process process = Process.Start(psi))
			{
				if (process == null) throw new InvalidOperationException("GPU çeviri işlemi başlatılamadı.");
				process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
				{
					if (string.IsNullOrWhiteSpace(e.Data)) return;
					string[] parts = e.Data.Split('|');
					if (parts.Length >= 3 && parts[0] == "PROGRESS" && int.TryParse(parts[1], out var done) && int.TryParse(parts[2], out var total))
						progress?.Invoke(done, total, e.Data);
					else
						progress?.Invoke(0, texts.Count, e.Data);
				};
				process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
				{
					if (!string.IsNullOrWhiteSpace(e.Data))
					{
						lock (errors) { if (errors.Length < 8000) errors.AppendLine(e.Data); }
					}
				};
				process.BeginOutputReadLine();
				process.BeginErrorReadLine();
				using (ct.Register(delegate { try { if (!process.HasExited) process.Kill(); } catch { } }))
				{
					await Task.Run(delegate { process.WaitForExit(); }).ConfigureAwait(false);
				}
				ct.ThrowIfCancellationRequested();
				if (process.ExitCode != 0) throw new Exception("GPU çevirisi başarısız (kod " + process.ExitCode + "). " + errors.ToString().Trim());
			}

			Result[] ordered = new Result[texts.Count];
			if (File.Exists(output))
			{
				foreach (string line in File.ReadLines(output, Encoding.UTF8))
				{
					if (string.IsNullOrWhiteSpace(line)) continue;
					try
					{
						Dictionary<string, object> row = json.DeserializeObject(line) as Dictionary<string, object>;
						if (row == null || !row.TryGetValue("key", out var keyValue) || !int.TryParse(Convert.ToString(keyValue), out var index) || index < 0 || index >= ordered.Length) continue;
						ordered[index] = new Result
						{
							Index = index,
							Translation = row.TryGetValue("translation", out var value) ? Convert.ToString(value) : "",
							QualityProblem = row.TryGetValue("worker_quality", out var quality) ? Convert.ToString(quality) : ""
						};
					}
					catch
					{
					}
				}
			}
			List<Result> results = new List<Result>(texts.Count);
			for (int i = 0; i < ordered.Length; i++)
			{
				results.Add(ordered[i] ?? new Result { Index = i, Translation = texts[i], QualityProblem = "GPU sonucu eksik" });
			}
			return results;
		}
		finally
		{
			try { Directory.Delete(job, true); } catch { }
		}
	}

	private static string Classify(string text)
	{
		string value = (text ?? "").Trim();
		if (value.Length <= 42 && value.IndexOfAny(new char[3] { '.', '!', '?' }) < 0) return "ARAYUZ";
		if (StartsWithAny(value, "Defeat ", "Collect ", "Talk to ", "Find ", "Use ", "Bring ", "Return to ")) return "GOREV_HEDEF";
		string lower = value.ToLowerInvariant();
		if (lower.Contains("damage") || lower.Contains("morale") || lower.Contains("armour") || lower.Contains("cooldown") || lower.Contains("%")) return "MEKANIK_BUFF_ACIKLAMA";
		if (value.Length >= 150 || value.Contains("\n") || value.Contains("\\n")) return "DIYALOG_HIKAYE";
		return "GENEL_OYUN_METNI";
	}

	private static bool StartsWithAny(string value, params string[] prefixes)
	{
		foreach (string prefix in prefixes)
		{
			if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
		}
		return false;
	}

	private static string Quote(string value) => "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
}
