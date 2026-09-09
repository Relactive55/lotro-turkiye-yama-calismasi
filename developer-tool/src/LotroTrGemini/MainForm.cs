using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LotroTrGemini;

public sealed class MainForm : Form
{
	private readonly AppSettings _cfg;

	private readonly TmStore _tm;

	private readonly ApprovedTranslationStore _approved;

	private TextBox _txtDat;

	private TextBox _txtKey;

	private TextBox _txtFilter;

	private TextBox _liveEn;

	private TextBox _liveTr;

	private Button _btnBrowse;

	private Button _btnLoadTexts;

	private Button _btnTranslate;

	private Button _btnGpuTranslate;

	private Button _btnApplyTxt;

	private Button _btnUpdate;

	private Button _btnSave;

	private Button _btnFilter;

	private Button _btnClearTm;

	private TrackBar _trkWorkers;

	private DataGridView _grid;

	private ProgressBar _bar;

	private Label _lblOrig;

	private Label _lblTr;

	private Label _credit;

	private Label _lblWorkers;

	private Label _lblEta;

	private Label _lblBanner;

	private LinkLabel _ytLink;

	private string _datPath;

	// ORJİNAL DAT klasörüne bırakılan dosya çoğu zaman temiz İngilizce dosya
	// değildir; çalışan Türkçe yamanın oyun güncelleyicisi tarafından
	// güncellenmiş halidir. Bu durumda kaynak dosya tek doğru kapsayıcıdır.
	private bool _sourceAlreadyTranslated;

	private int _sourceTurkishRows;

	private int _approvedApplied;

	private readonly List<LocRow> _rows = new List<LocRow>();

	private List<int> _view = new List<int>();

	private readonly Dictionary<int, LocBin> _bins = new Dictionary<int, LocBin>();

	private readonly Dictionary<int, DatEntry> _meta = new Dictionary<int, DatEntry>();

	private readonly Dictionary<int, byte[]> _originalRaw = new Dictionary<int, byte[]>();

	private readonly Dictionary<int, bool> _compressed = new Dictionary<int, bool>();

	/// <summary>TR baseline for session dirty detect.</summary>
	private readonly Dictionary<string, string> _trBaseline = new Dictionary<string, string>(StringComparer.Ordinal);

	private readonly HashSet<string> _knownReferenceRows = new HashSet<string>(StringComparer.Ordinal);

	private CancellationTokenSource _cts;

	private ManualResetEventSlim _pauseGate = new ManualResetEventSlim(initialState: true);

	private bool _busy;

	private LocRow _boundRow;

	private bool _editorLoading;

	private bool _editorDirty;

	private string _editorBuffer = "";

	private volatile int _workerLimit = 2;

	private int _activeWorkers;

	private readonly object _workerLock = new object();

	private DateTime _loadedDatWriteTimeUtc = DateTime.MinValue;

	private long _loadedDatLength = -1L;

	public MainForm()
	{
		_cfg = new AppSettings(Path.Combine(Program.AppDir, "settings.ini"));
		string path = Path.Combine(Program.AppDir, "tm.tsv");
		string otherPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tm.tsv");
		_tm = new TmStore(path);
		_approved = new ApprovedTranslationStore(Path.Combine(Program.AppDir, "approved_updates.tsv"));
		try
		{
			if (_tm.Count == 0)
			{
				_tm.MergeFrom(otherPath);
			}
		}
		catch
		{
		}
		Text = "Lord of the Rings Türkçe Çeviri Aracı - Relactive";
		base.Width = 1220;
		base.Height = 820;
		MinimumSize = new Size(1040, 660);
		base.StartPosition = FormStartPosition.CenterScreen;
		Font = new Font("Segoe UI", 9.25f);
		BackColor = Color.FromArgb(248, 246, 241);
		ForeColor = Color.FromArgb(28, 32, 36);
		BuildUi();
		WireEvents();
		TryPreselectPaths();
		base.FormClosing += delegate
		{
			try
			{
				_tm.Save();
			}
			catch
			{
			}
		};
	}

	private void TryPreselectPaths()
	{
		try
		{
			string text = ProjPaths.FindEnDat();
			if (!string.IsNullOrEmpty(text) && File.Exists(text))
			{
				_datPath = text;
				_txtDat.Text = _datPath;
				_cfg.LastDatPath = text;
				_cfg.LastDatFolder = Path.GetDirectoryName(text);
				_cfg.Save();
				return;
			}
			if (!string.IsNullOrEmpty(_cfg.LastDatPath) && File.Exists(_cfg.LastDatPath))
			{
				_datPath = _cfg.LastDatPath;
				_txtDat.Text = _datPath;
			}
		}
		catch
		{
		}
	}

	private void BuildUi()
	{
		Panel header = new Panel
		{
			Left = 0,
			Top = 0,
			Width = base.ClientSize.Width,
			Height = 52,
			Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right),
			BackColor = Color.FromArgb(34, 48, 42)
		};
		_txtDat = new TextBox
		{
			Left = 12,
			Top = 12,
			Width = 860,
			Height = 26,
			ReadOnly = true,
			BackColor = Color.FromArgb(245, 243, 236),
			Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right),
			BorderStyle = BorderStyle.FixedSingle
		};
		_btnBrowse = new Button
		{
			Left = 886,
			Top = 10,
			Width = 140,
			Height = 30,
			Text = "Dosya Seç",
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.FromArgb(196, 168, 102),
			ForeColor = Color.FromArgb(28, 32, 36),
			Anchor = (AnchorStyles.Top | AnchorStyles.Right)
		};
		header.Controls.AddRange(new Control[2] { _txtDat, _btnBrowse });
		header.Resize += delegate
		{
			_btnBrowse.Left = header.Width - 152;
			_txtDat.Width = Math.Max(200, _btnBrowse.Left - 24);
		};
		int num = 62;
		_btnLoadTexts = new Button
		{
			Left = 12,
			Top = num,
			Width = 120,
			Height = 32,
			Text = "Metinleri yükle",
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.FromArgb(70, 110, 150),
			ForeColor = Color.White
		};
		_btnTranslate = null;
		_btnGpuTranslate = new Button
		{
			Left = 128,
			Top = num,
			Width = 185,
			Height = 32,
			Text = "GPU Çeviri · Kalite",
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.FromArgb(56, 120, 84),
			ForeColor = Color.White
		};
		_btnApplyTxt = null;
		_btnUpdate = null;
		_btnSave = new Button
		{
			Left = 319,
			Top = num,
			Width = 75,
			Height = 32,
			Text = "DAT yaz",
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.FromArgb(196, 168, 102)
		};
		_btnClearTm = new Button
		{
			Left = 400,
			Top = num,
			Width = 80,
			Height = 32,
			Text = "Bellek sil",
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.FromArgb(160, 80, 70),
			ForeColor = Color.White,
			Font = new Font("Segoe UI", 8f)
		};
		int value = (_workerLimit = Math.Max(1, Math.Min(20, (_cfg.Workers > 0) ? _cfg.Workers : 12)));
		_lblWorkers = new Label
		{
			Left = 490,
			Top = num + 6,
			Width = 250,
			Height = 22,
			Text = "NVIDIA · OPUS + Qwen3 kalite",
			Font = new Font("Segoe UI", 9f, FontStyle.Bold),
			ForeColor = Color.FromArgb(40, 50, 45)
		};
		_trkWorkers = new TrackBar
		{
			Left = 637,
			Top = num - 2,
			Width = 100,
			Height = 36,
			Minimum = 1,
			Maximum = 20,
			TickFrequency = 1,
			Value = value,
			SmallChange = 1,
			LargeChange = 2,
			Visible = false
		};
		Label label = new Label
		{
			Left = 770,
			Top = num + 6,
			Width = 36,
			Height = 22,
			Text = "Ara:",
			Anchor = (AnchorStyles.Top | AnchorStyles.Right)
		};
		_txtFilter = new TextBox
		{
			Left = 808,
			Top = num + 4,
			Width = 180,
			Height = 24,
			Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right),
			BorderStyle = BorderStyle.FixedSingle
		};
		_btnFilter = new Button
		{
			Left = 996,
			Top = num,
			Width = 70,
			Height = 32,
			Text = "Bul",
			FlatStyle = FlatStyle.Flat,
			Anchor = (AnchorStyles.Top | AnchorStyles.Right)
		};
		_lblBanner = new Label
		{
			Left = 12,
			Top = 100,
			Width = 1190,
			Height = 22,
			Text = "",
			Font = new Font("Segoe UI Semibold", 11f),
			ForeColor = Color.FromArgb(30, 90, 50),
			Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right)
		};
		_txtKey = new TextBox
		{
			Visible = false,
			Width = 0,
			Height = 0
		};
		_grid = new DataGridView
		{
			Left = 12,
			Top = 126,
			Width = 1190,
			Height = 400,
			Anchor = (AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right),
			AllowUserToAddRows = false,
			AllowUserToDeleteRows = false,
			RowHeadersVisible = false,
			SelectionMode = DataGridViewSelectionMode.FullRowSelect,
			AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
			VirtualMode = true,
			BackgroundColor = Color.White,
			BorderStyle = BorderStyle.FixedSingle,
			GridColor = Color.FromArgb(220, 216, 205),
			ColumnHeadersVisible = false,
			EditMode = DataGridViewEditMode.EditProgrammatically
		};
		_grid.Columns.Add(new DataGridViewTextBoxColumn
		{
			Name = "key",
			HeaderText = "",
			FillWeight = 16f,
			ReadOnly = true
		});
		_grid.Columns.Add(new DataGridViewTextBoxColumn
		{
			Name = "en",
			HeaderText = "",
			FillWeight = 42f,
			ReadOnly = true
		});
		_grid.Columns.Add(new DataGridViewTextBoxColumn
		{
			Name = "tr",
			HeaderText = "",
			FillWeight = 42f,
			ReadOnly = true
		});
		_lblOrig = new Label
		{
			Left = 12,
			Top = 540,
			Width = 200,
			Height = 18,
			Text = "Orijinal",
			ForeColor = Color.FromArgb(60, 60, 60)
		};
		_lblTr = new Label
		{
			Left = 612,
			Top = 540,
			Width = 280,
			Height = 18,
			Text = "Çevrilen — alt kutuda düzelt, satır değişince kaydedilir",
			ForeColor = Color.FromArgb(60, 60, 60)
		};
		_liveEn = new TextBox
		{
			Left = 12,
			Top = 560,
			Width = 580,
			Height = 96,
			Multiline = true,
			ReadOnly = true,
			ScrollBars = ScrollBars.Vertical,
			BackColor = Color.FromArgb(245, 243, 236),
			BorderStyle = BorderStyle.FixedSingle
		};
		_liveTr = new TextBox
		{
			Left = 612,
			Top = 560,
			Width = 580,
			Height = 96,
			Multiline = true,
			ReadOnly = false,
			ScrollBars = ScrollBars.Vertical,
			BackColor = Color.FromArgb(236, 245, 236),
			BorderStyle = BorderStyle.FixedSingle
		};
		_lblEta = new Label
		{
			Left = 12,
			Top = 668,
			Width = 1190,
			Height = 18,
			Text = "ETA: —",
			Font = new Font("Segoe UI", 9f),
			ForeColor = Color.FromArgb(40, 40, 40)
		};
		_bar = new ProgressBar
		{
			Left = 12,
			Top = 690,
			Width = 1190,
			Height = 16,
			Style = ProgressBarStyle.Continuous
		};
		_ytLink = new LinkLabel
		{
			AutoSize = true,
			Text = "https://www.youtube.com/@Relactive55",
			LinkColor = Color.FromArgb(40, 90, 150),
			ActiveLinkColor = Color.FromArgb(20, 60, 110),
			VisitedLinkColor = Color.FromArgb(40, 90, 150),
			Font = new Font("Segoe UI", 8.5f),
			Cursor = Cursors.Hand
		};
		_ytLink.Links.Clear();
		_ytLink.Links.Add(0, _ytLink.Text.Length, "https://www.youtube.com/@Relactive55");
		_credit = new Label
		{
			AutoSize = true,
			Text = "Yapım Relactive",
			ForeColor = Color.FromArgb(90, 90, 90),
			Font = new Font("Segoe UI", 8.5f)
		};
		base.Controls.Add(header);
		base.Controls.AddRange(new Control[17]
		{
			_btnLoadTexts, _btnGpuTranslate, _btnSave, _btnClearTm, _lblWorkers, label, _txtFilter, _btnFilter, _lblBanner,
			_grid, _lblOrig, _lblTr, _liveEn, _liveTr, _lblEta, _bar, _ytLink
		});
		base.Controls.Add(_credit);
		Control[] array = new Control[8] { _lblOrig, _lblTr, _liveEn, _liveTr, _lblEta, _bar, _ytLink, _credit };
		for (int num2 = 0; num2 < array.Length; num2++)
		{
			array[num2].Anchor = AnchorStyles.None;
		}
		base.Resize += delegate
		{
			LayoutBottom();
		};
		base.Shown += delegate
		{
			LayoutBottom();
		};
		LayoutBottom();
		RefreshWorkerLabel();
	}

	private void LayoutBottom()
	{
		if (_liveEn == null || _liveTr == null)
		{
			return;
		}
		int num = base.ClientSize.Width;
		int num2 = base.ClientSize.Height;
		if (num < 200 || num2 < 200)
		{
			return;
		}
		if (_btnFilter != null)
		{
			_btnFilter.Left = num - 82;
			if (_txtFilter != null)
			{
				int num3 = 750;
				Label label = base.Controls.OfType<Label>().FirstOrDefault((Label l) => l.Text == "Ara:");
				if (label != null)
				{
					label.Left = num3;
					_txtFilter.Left = num3 + 40;
				}
				else
				{
					_txtFilter.Left = num3;
				}
				_txtFilter.Width = Math.Max(60, _btnFilter.Left - _txtFilter.Left - 8);
			}
		}
		if (_lblBanner != null)
		{
			_lblBanner.Width = num - 24;
		}
		int num4 = 22;
		int num5 = 16;
		int num6 = 18;
		int num7 = 6;
		int num8 = num2 - num4 - 6;
		int num9 = num8 - num7 - num5;
		int num10 = num9 - num7 - num6;
		if (_ytLink != null)
		{
			_ytLink.Left = 12;
			_ytLink.Top = num8;
			_ytLink.BringToFront();
		}
		if (_credit != null)
		{
			_credit.Left = num - _credit.PreferredWidth - 12;
			_credit.Top = num8;
			_credit.BringToFront();
		}
		if (_bar != null)
		{
			_bar.Left = 12;
			_bar.Top = num9;
			_bar.Width = Math.Max(100, num - 24);
			_bar.Height = num5;
		}
		if (_lblEta != null)
		{
			_lblEta.Left = 12;
			_lblEta.Top = num10;
			_lblEta.Width = num - 24;
			_lblEta.Height = num6;
		}
		int num11 = 96;
		int num12 = num10 - num7 - num11 - 20;
		if (num12 < 200)
		{
			num12 = 200;
		}
		int num13 = Math.Max(180, (num - 36) / 2);
		_lblOrig.Left = 12;
		_lblOrig.Top = num12 - 20;
		_liveEn.Left = 12;
		_liveEn.Top = num12;
		_liveEn.Width = num13;
		_liveEn.Height = num11;
		_lblTr.Left = 24 + num13;
		_lblTr.Top = num12 - 20;
		_liveTr.Left = 24 + num13;
		_liveTr.Top = num12;
		_liveTr.Width = num13;
		_liveTr.Height = num11;
		if (_grid != null)
		{
			_grid.Left = 12;
			_grid.Top = 126;
			_grid.Width = num - 24;
			_grid.Height = Math.Max(100, num12 - 22 - _grid.Top);
		}
	}

	private static string WorkerLabelText(int limit, int active)
	{
		return "NVIDIA · OPUS + Qwen3 kalite";
	}

	private void RefreshWorkerLabel()
	{
		if (_lblWorkers != null)
		{
			int num = Math.Max(1, Math.Min(20, _workerLimit));
			int activeWorkers;
			lock (_workerLock)
			{
				activeWorkers = _activeWorkers;
			}
			if (activeWorkers > 0)
			{
				_lblWorkers.Text = "GPU kalite çevirisi çalışıyor";
			}
			else
			{
				_lblWorkers.Text = WorkerLabelText(num, 0);
			}
		}
	}

	private void WireEvents()
	{
		_btnBrowse.Click += delegate
		{
			BrowseDatPathOnly();
		};
		if (_btnLoadTexts != null)
		{
			_btnLoadTexts.Click += async delegate
			{
				await LoadTextsClickedAsync();
			};
		}
		_btnGpuTranslate.Click += async delegate
		{
			TryPreselectPaths();
			if (_rows.Count == 0 || DatChangedSinceLoad())
			{
				await LoadDatAsync();
			}
			await TranslateGpuAsync();
		};
		_btnSave.Click += async delegate
		{
			await SaveDatAsync();
		};
		if (_btnClearTm != null)
		{
			_btnClearTm.Click += delegate
			{
				ClearTranslationMemory();
			};
		}
		if (_btnFilter != null)
		{
			_btnFilter.Click += delegate
			{
				ApplyFilter();
			};
		}
		if (_txtFilter != null)
		{
			_txtFilter.KeyDown += delegate(object s, KeyEventArgs e)
			{
				if (e.KeyCode == Keys.Return)
				{
					ApplyFilter();
					e.SuppressKeyPress = true;
				}
			};
		}
		_trkWorkers.ValueChanged += delegate
		{
			int workers = (_workerLimit = _trkWorkers.Value);
			_cfg.Workers = workers;
			try
			{
				_cfg.Save();
			}
			catch
			{
			}
			RefreshWorkerLabel();
		};
		base.FormClosing += delegate
		{
			try
			{
				FlushBoundEditor();
				_pauseGate.Set();
				if (_cts != null)
				{
					_cts.Cancel();
				}
				_cfg.Workers = Math.Max(1, _workerLimit);
				_cfg.Save();
			}
			catch
			{
			}
		};
		_grid.CellValueNeeded += delegate(object s, DataGridViewCellValueEventArgs e)
		{
			if (e.RowIndex >= 0 && e.RowIndex < _view.Count)
			{
				LocRow locRow = _rows[_view[e.RowIndex]];
				if (e.ColumnIndex == 0)
				{
					e.Value = locRow.Key;
				}
				else if (e.ColumnIndex == 1)
				{
					e.Value = locRow.Original;
				}
				else if (e.ColumnIndex == 2)
				{
					if (_boundRow != null && _boundRow == locRow && _editorDirty)
					{
						e.Value = _editorBuffer;
					}
					else
					{
						e.Value = locRow.Translation;
					}
				}
			}
		};
		_grid.SelectionChanged += delegate
		{
			SyncBoundRowFromSelection();
		};
		_grid.CurrentCellChanged += delegate
		{
			SyncBoundRowFromSelection();
		};
		_liveTr.Leave += delegate
		{
			FlushBoundEditor();
		};
		_liveTr.TextChanged += delegate
		{
			if (_editorLoading || _busy || _liveTr == null || _boundRow == null)
			{
				return;
			}
			_editorBuffer = _liveTr.Text ?? "";
			_editorDirty = true;
			try
			{
				int num = IndexInView(_boundRow);
				if (num >= 0)
				{
					_grid.InvalidateRow(num);
				}
			}
			catch
			{
			}
		};
		if (_ytLink == null)
		{
			return;
		}
		_ytLink.LinkClicked += delegate(object s, LinkLabelLinkClickedEventArgs e)
		{
			try
			{
				string fileName = (e.Link.LinkData as string) ?? "https://www.youtube.com/@Relactive55";
				Process.Start(new ProcessStartInfo
				{
					FileName = fileName,
					UseShellExecute = true
				});
			}
			catch
			{
				try
				{
					Clipboard.SetText("https://www.youtube.com/@Relactive55");
				}
				catch
				{
				}
			}
		};
	}

	private void SyncBoundRowFromSelection()
	{
		if (_grid == null || _rows.Count == 0)
		{
			return;
		}
		LocRow locRow = null;
		if (_grid.CurrentCell != null)
		{
			int rowIndex = _grid.CurrentCell.RowIndex;
			if (rowIndex >= 0 && rowIndex < _view.Count)
			{
				locRow = _rows[_view[rowIndex]];
			}
		}
		if (_boundRow != locRow)
		{
			FlushBoundEditor();
			BindEditor(locRow);
		}
	}

	private void BindEditor(LocRow row)
	{
		_boundRow = row;
		_editorDirty = false;
		_editorBuffer = ((row != null) ? (row.Translation ?? "") : "");
		_editorLoading = true;
		try
		{
			if (_txtKey != null)
			{
				_txtKey.Text = ((row != null) ? (row.Key ?? "") : "");
			}
			if (_liveEn != null)
			{
				_liveEn.Text = ((row != null) ? (row.Original ?? "") : "");
			}
			if (_liveTr != null)
			{
				_liveTr.Text = _editorBuffer;
			}
		}
		finally
		{
			_editorLoading = false;
		}
	}

	private void FlushBoundEditor()
	{
		if (_boundRow == null || !_editorDirty)
		{
			_editorDirty = false;
			return;
		}
		string text = _boundRow.Original ?? "";
		string text2 = _editorBuffer ?? "";
		if (TranslationQuality.LooksCorruptPair(text, text2))
		{
			_editorDirty = false;
			_editorLoading = true;
			try
			{
				if (_liveTr != null)
				{
					_liveTr.Text = _boundRow.Translation ?? "";
				}
				_editorBuffer = _boundRow.Translation ?? "";
				return;
			}
			finally
			{
				_editorLoading = false;
			}
		}
		if (!string.Equals(_boundRow.Translation, text2, StringComparison.Ordinal))
		{
			_boundRow.Translation = text2;
		}
		if (!string.IsNullOrEmpty(text) && text2.Length > 0 && !string.Equals(text, text2, StringComparison.Ordinal))
		{
			_tm.Upsert(text, text2);
			try
			{
				_tm.Save();
			}
			catch
			{
			}
		}
		_editorDirty = false;
		try
		{
			int num = IndexInView(_boundRow);
			if (num >= 0)
			{
				_grid.InvalidateRow(num);
			}
		}
		catch
		{
		}
	}

	private int IndexInView(LocRow row)
	{
		if (row == null || _view == null)
		{
			return -1;
		}
		for (int i = 0; i < _view.Count; i++)
		{
			if (_rows[_view[i]] == row)
			{
				return i;
			}
		}
		return -1;
	}

	private void CommitLiveTranslation(bool force = false)
	{
		if (force)
		{
			_editorDirty = true;
		}
		FlushBoundEditor();
	}

	private void SetBusy(bool b)
	{
		if (b)
		{
			try
			{
				FlushBoundEditor();
			}
			catch
			{
			}
			_editorDirty = false;
		}
		_busy = b;
		if (_btnBrowse != null)
		{
			_btnBrowse.Enabled = !b;
		}
		if (_btnLoadTexts != null)
		{
			_btnLoadTexts.Enabled = !b;
		}
		if (_btnTranslate != null)
		{
			_btnTranslate.Enabled = !b;
		}
		if (_btnGpuTranslate != null)
		{
			_btnGpuTranslate.Enabled = !b;
		}
		if (_btnApplyTxt != null)
		{
			_btnApplyTxt.Enabled = !b;
		}
		if (_btnUpdate != null)
		{
			_btnUpdate.Enabled = !b;
		}
		if (_btnSave != null)
		{
			_btnSave.Enabled = !b;
		}
		if (_btnClearTm != null)
		{
			_btnClearTm.Enabled = !b;
		}
		if (_btnFilter != null)
		{
			_btnFilter.Enabled = !b;
		}
		if (_txtFilter != null)
		{
			_txtFilter.Enabled = !b;
		}
		if (_trkWorkers != null)
		{
			_trkWorkers.Enabled = true;
		}
		if (_liveTr != null)
		{
			_liveTr.ReadOnly = b;
		}
		if (_grid != null)
		{
			_grid.ReadOnly = b;
		}
		if (!b)
		{
			_pauseGate.Set();
			lock (_workerLock)
			{
				_activeWorkers = 0;
			}
			RefreshWorkerLabel();
		}
	}

	private void ApplyFilter()
	{
		string text = ((_txtFilter != null) ? (_txtFilter.Text ?? "").Trim() : "");
		_view = new List<int>();
		if (text.Length == 0)
		{
			for (int i = 0; i < _rows.Count; i++)
			{
				_view.Add(i);
			}
		}
		else
		{
			for (int j = 0; j < _rows.Count; j++)
			{
				LocRow locRow = _rows[j];
				if ((locRow.Original != null && locRow.Original.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) || (locRow.Translation != null && locRow.Translation.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) || (locRow.Key != null && locRow.Key.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0))
				{
					_view.Add(j);
				}
			}
		}
		_grid.RowCount = 0;
		_grid.RowCount = _view.Count;
		if (_grid.CurrentCell != null && _grid.CurrentCell.RowIndex >= 0 && _grid.CurrentCell.RowIndex < _view.Count)
		{
			FlushBoundEditor();
			BindEditor(_rows[_view[_grid.CurrentCell.RowIndex]]);
		}
		else
		{
			FlushBoundEditor();
			BindEditor(null);
		}
		Status($"Ara: {_view.Count:N0} / {_rows.Count:N0}");
	}

	private void SetBanner(string text, Color? color = null)
	{
		if (_lblBanner == null)
		{
			return;
		}
		if (base.InvokeRequired)
		{
			try
			{
				BeginInvoke((Action)delegate
				{
					SetBanner(text, color);
				});
				return;
			}
			catch
			{
				return;
			}
		}
		_lblBanner.Text = text ?? "";
		if (color.HasValue)
		{
			_lblBanner.ForeColor = color.Value;
		}
		else
		{
			_lblBanner.ForeColor = Color.FromArgb(30, 90, 50);
		}
	}

	private void Status(string s)
	{
		try
		{
			if (!string.IsNullOrEmpty(s))
			{
				Program.Log("status " + s);
			}
		}
		catch
		{
		}
	}

	private void Stat(string s)
	{
		try
		{
			if (!string.IsNullOrEmpty(s))
			{
				Program.Log("stat " + s);
			}
		}
		catch
		{
		}
	}

	private void Eta(string s)
	{
		if (_lblEta == null)
		{
			return;
		}
		if (base.InvokeRequired)
		{
			try
			{
				BeginInvoke((Action)delegate
				{
					Eta(s);
				});
				return;
			}
			catch
			{
				return;
			}
		}
		_lblEta.Text = (string.IsNullOrEmpty(s) ? "ETA: —" : s);
	}

	private void ClearTranslationMemory()
	{
		if (_busy)
		{
			MessageBox.Show(this, "İşlem sürerken bellek silinemez.", "Bellek", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		switch (MessageBox.Show(this, "Yes = Tüm Türkçeyi EN'e sıfırla + bellek sil\r\nNo = Sadece kaymış satırları temizle (aynı metin birçok yere yapıştıysa)\r\nİptal = vazgeç", "Bellek / bozulma", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Exclamation))
		{
		case DialogResult.Cancel:
			return;
		case DialogResult.No:
		{
			int num = PurgeCorruptTranslations();
			try
			{
				_tm.ClearAll();
			}
			catch
			{
			}
			TryDeleteTmFiles(Program.AppDir);
			if (_grid != null)
			{
				_grid.Invalidate();
				_grid.Refresh();
			}
			MessageBox.Show(this, "Bozuk eşleşme temizlendi: " + num.ToString("N0") + " satır.\r\nTM silindi. Otomatik çeviriyi tekrar çalıştır.", "Temiz");
			return;
		}
		}
		try
		{
			_tm.ClearAll();
			TryDeleteTmFiles(Program.AppDir);
			TryDeleteTmFiles(AppDomain.CurrentDomain.BaseDirectory);
			int num2 = 0;
			for (int i = 0; i < _rows.Count; i++)
			{
				LocRow locRow = _rows[i];
				string text = locRow.Original ?? "";
				if (!string.Equals(locRow.Translation, text, StringComparison.Ordinal))
				{
					num2++;
				}
				locRow.Translation = text;
			}
			try
			{
				if (_grid != null)
				{
					_grid.Invalidate();
					_grid.Refresh();
				}
				_editorDirty = false;
				if (_boundRow != null)
				{
					BindEditor(_boundRow);
				}
				else
				{
					BindEditor(null);
				}
			}
			catch
			{
			}
			int num3 = 0;
			for (int j = 0; j < _rows.Count && j < 400; j++)
			{
				if (LooksMostlyTurkish(_rows[j].Original ?? ""))
				{
					num3++;
				}
			}
			string text2 = "";
			if (num3 > 80)
			{
				text2 = "\r\n\r\nUYARI: Orijinal sütunda da Türkçe metin var.\r\nMuhtemelen TR paket DAT açtınız, EN orijinal değil!\r\nDosya Seç → DAT DOSYA\\ORJİNAL\\client_local_English.dat";
			}
			SetBanner("Bellek + tablo TR silindi (0) — çevrilecek satırlar EN");
			Status("Bellek silindi; satırlar orijinale sıfırlandı: " + num2.ToString("N0"));
			MessageBox.Show(this, "Bellek silindi.\r\nTablo Türkçe sütunu orijinale döndü (" + num2.ToString("N0") + " satır)." + text2 + "\r\n\r\nİstersen DAT’ı yeniden yükle; TM vuruşu 0 olmalı.", "Bellek", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "Bellek sil hatası");
		}
	}

	private int PurgeCorruptTranslations()
	{
		if (_rows == null || _rows.Count == 0)
		{
			return 0;
		}
		int num = 0;
		for (int i = 0; i < _rows.Count; i++)
		{
			LocRow locRow = _rows[i];
			string text = locRow.Original ?? "";
			string tr = locRow.Translation ?? "";
			if (TranslationQuality.LooksCorruptPair(text, tr))
			{
				locRow.Translation = text;
				num++;
			}
		}
		Dictionary<string, List<int>> dictionary = new Dictionary<string, List<int>>(StringComparer.Ordinal);
		for (int j = 0; j < _rows.Count; j++)
		{
			LocRow locRow2 = _rows[j];
			string a = locRow2.Original ?? "";
			string text2 = locRow2.Translation ?? "";
			if (!string.IsNullOrEmpty(text2) && text2.Length >= 50 && !string.Equals(a, text2, StringComparison.Ordinal))
			{
				if (!dictionary.TryGetValue(text2, out var value))
				{
					List<int> list = (dictionary[text2] = new List<int>());
					value = list;
				}
				value.Add(j);
			}
		}
		foreach (KeyValuePair<string, List<int>> item in dictionary)
		{
			if (item.Value.Count < 8)
			{
				continue;
			}
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
			foreach (int item2 in item.Value)
			{
				hashSet.Add(_rows[item2].Original ?? "");
			}
			if (hashSet.Count < 8)
			{
				continue;
			}
			foreach (int item3 in item.Value)
			{
				LocRow locRow3 = _rows[item3];
				string text3 = locRow3.Original ?? "";
				if (!string.Equals(locRow3.Translation, text3, StringComparison.Ordinal))
				{
					locRow3.Translation = text3;
					num++;
				}
			}
		}
		try
		{
			if (num > 0 && _grid != null)
			{
				_grid.Invalidate();
			}
		}
		catch
		{
		}
		return num;
	}

	private static void TryDeleteTmFiles(string dir)
	{
		if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
		{
			return;
		}
		string[] array = new string[2] { "tm.tsv", "tm.tsv.tmp" };
		foreach (string path in array)
		{
			try
			{
				string path2 = Path.Combine(dir, path);
				if (File.Exists(path2))
				{
					File.Delete(path2);
				}
			}
			catch
			{
			}
		}
	}

	private static bool LooksMostlyTurkish(string s)
	{
		if (string.IsNullOrEmpty(s) || s.Length < 4)
		{
			return false;
		}
		int num = 0;
		foreach (char value in s)
		{
			if ("çğıöşüÇĞİÖŞÜ".IndexOf(value) >= 0)
			{
				num++;
			}
		}
		return num >= 2;
	}

	private void BrowseDatPathOnly()
	{
		using OpenFileDialog openFileDialog = new OpenFileDialog();
		openFileDialog.Filter = "client_local_English.dat|client_local_English.dat|DAT (*.dat)|*.dat|All|*.*";
		openFileDialog.Title = "client_local_English.dat dosyasını seçin (Önce yedekle)";
		openFileDialog.FileName = "client_local_English.dat";
		string text = ProjPaths.FindEnDat();
		if (!string.IsNullOrEmpty(text) && File.Exists(text))
		{
			openFileDialog.InitialDirectory = Path.GetDirectoryName(text);
		}
		else if (!string.IsNullOrEmpty(_cfg.LastDatFolder) && Directory.Exists(_cfg.LastDatFolder))
		{
			openFileDialog.InitialDirectory = _cfg.LastDatFolder;
		}
		else if (Directory.Exists(ProjPaths.DatRoot))
		{
			openFileDialog.InitialDirectory = ProjPaths.DatRoot;
		}
		if (openFileDialog.ShowDialog(this) == DialogResult.OK)
		{
			_datPath = openFileDialog.FileName;
			_txtDat.Text = _datPath;
			_cfg.LastDatPath = _datPath;
			_cfg.LastDatFolder = Path.GetDirectoryName(_datPath);
			_cfg.Save();
		}
	}

	private async Task LoadTextsClickedAsync()
	{
		string text = _datPath;
		if (string.IsNullOrWhiteSpace(text) && _txtDat != null)
		{
			text = (_txtDat.Text ?? "").Trim();
		}
		if (string.IsNullOrWhiteSpace(text) || !File.Exists(text))
		{
			MessageBox.Show(this, "Önce bir DAT dosyası seçin.\r\nDosya Seç ile client_local_English.dat yolunu belirleyin, sonra Metinleri yükle’ye basın.", "Metinleri yükle", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			BrowseDatPathOnly();
			text = _datPath;
			if (string.IsNullOrWhiteSpace(text) || !File.Exists(text))
			{
				return;
			}
		}
		else
		{
			_datPath = text;
			if (_txtDat != null)
			{
				_txtDat.Text = text;
			}
		}
		await LoadDatAsync();
	}

	private async Task EnterWorkerAsync(CancellationToken ct)
	{
		while (true)
		{
			ct.ThrowIfCancellationRequested();
			bool flag = false;
			lock (_workerLock)
			{
				int num = Math.Max(1, _workerLimit);
				if (_activeWorkers < num)
				{
					_activeWorkers++;
					flag = true;
				}
			}
			if (flag)
			{
				break;
			}
			await Task.Delay(15, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		try
		{
			if (base.IsHandleCreated && !base.IsDisposed)
			{
				BeginInvoke(new Action(RefreshWorkerLabel));
			}
		}
		catch
		{
		}
	}

	private void LeaveWorker()
	{
		lock (_workerLock)
		{
			if (_activeWorkers > 0)
			{
				_activeWorkers--;
			}
		}
		try
		{
			if (base.IsHandleCreated && !base.IsDisposed)
			{
				BeginInvoke(new Action(RefreshWorkerLabel));
			}
		}
		catch
		{
		}
	}

	private void Live(string en, string tr)
	{
		try
		{
			if (!string.IsNullOrEmpty(en))
			{
				string text = ((en.Length > 80) ? (en.Substring(0, 80) + "…") : en);
				Stat("… " + text);
			}
		}
		catch
		{
		}
	}

	private async Task LoadDatAsync()
	{
		if (string.IsNullOrEmpty(_datPath) || !File.Exists(_datPath))
		{
			MessageBox.Show(this, "Önce client_local_English.dat seçin (yedek alın).");
			return;
		}
		FileInfo loadStartInfo = new FileInfo(_datPath);
		long loadStartLength = loadStartInfo.Length;
		DateTime loadStartWriteTimeUtc = loadStartInfo.LastWriteTimeUtc;
		SetBusy(b: true);
		SetBanner("Yükleniyor…", Color.FromArgb(80, 80, 40));
		Eta("ETA: —");
		if (_bar != null)
		{
			_bar.Value = 0;
			_bar.Maximum = 100;
		}
		_rows.Clear();
		_bins.Clear();
		_meta.Clear();
		_originalRaw.Clear();
		_compressed.Clear();
		_trBaseline.Clear();
		_knownReferenceRows.Clear();
		_view = new List<int>();
		_grid.RowCount = 0;
		_boundRow = null;
		_editorDirty = false;
		_editorBuffer = "";
		BindEditor(null);
		try
		{
			List<LocRow> rows = new List<LocRow>();
			int localizationSubfiles = 0;
			int trHit = 0;
			int tmHit = 0;
			int approvedHit = 0;
			int sourceTurkishRowsLocal = 0;
			bool sourceAlreadyTranslatedLocal = false;
			string trPackagePath = null;
			Dictionary<string, string> trBaselineLocal = new Dictionary<string, string>(StringComparer.Ordinal);
			HashSet<string> knownReferenceRowsLocal = new HashSet<string>(StringComparer.Ordinal);
			await Task.Run(delegate
			{
				using DatExportSession nativeSource = new DatExportSession();
				nativeSource.Open(_datPath, writable: false);
				Dictionary<int, int[]> sourceSizes = nativeSource.LoadSizeMap();
				List<int> list = sourceSizes.Keys.Where(did => (uint)did >> 24 == 37).OrderBy(did => unchecked((uint)did)).ToList();
				localizationSubfiles = list.Count;
				Status(list.Count + " lokalizasyon alt dosya…");
				int num2 = 0;
				foreach (int did in list)
				{
					num2++;
					LocBin locBin;
					try
					{
						int version;
						byte[] data = nativeSource.ReadSubfile(did, sourceSizes[did][0], out version);
						locBin = LocBin.Parse(data, did);
					}
					catch
					{
						continue;
					}
					foreach (LocRow row in locBin.GetRows(did))
					{
						row.Translation = row.Original ?? "";
						rows.Add(row);
					}
					if (num2 % 40 == 0)
					{
						Status($"Yükleniyor {num2}/{list.Count} · {rows.Count:N0} satır");
						Stat("Alt dosya 0x" + did.ToString("X8"));
					}
				}
				sourceTurkishRowsLocal = rows.Count(row => TranslationQuality.LooksTurkishText(row.Original ?? ""));
				// Temiz İngilizce istemcide Tolkien adları nedeniyle az sayıda yanlış
				// Türkçe sinyali görülebilir. Beş bin satır eşiği, dağıtılmış Türkçe
				// yamayı güvenle ayırırken bu yanlış pozitifleri dışarıda bırakır.
				// LOTRO'nun temiz İngilizce kataloğunda özel adlar ve dil algılayıcısının
				// kısa metinlerdeki yanlış pozitifleri birkaç bin "Türkçe" sinyali
				// üretebiliyor. Kaynağı yalnızca belirgin bir katalog oranı Türkçe ise
				// çevrilmiş say; aksi durumda doğrulanmış TM'nin uygulanmasını engelleme.
				int translatedSourceThreshold = Math.Max(50000, rows.Count / 10);
				sourceAlreadyTranslatedLocal = sourceTurkishRowsLocal >= translatedSourceThreshold;
				trPackagePath = ProjPaths.FindTrDat();
				// Önceki Türkçe DAT yalnız arşiv/referans olarak tutulur. Güncelleme
				// sonrasında aynı anahtar başka bir anlama veya yapıya taşınabilir;
				// bu nedenle eski DAT'tan key-only metin aktarımı yapılmaz. Otomatik
				// uygulama yalnız kaynak metni de birebir doğrulayan TM ve onaylı
				// Key+Source+Target kuralları üzerinden ilerler.
				if (!string.IsNullOrEmpty(trPackagePath))
				{
					Program.Log($"LoadDat TR package reference-only path={trPackagePath}");
				}
			});
			if (!DatMatchesStamp(loadStartLength, loadStartWriteTimeUtc))
			{
				throw new IOException("Kaynak DAT yükleme sırasında değişti. Oyun güncellemesi bittikten sonra metinleri yeniden yükleyin.");
			}
			_sourceTurkishRows = sourceTurkishRowsLocal;
			_sourceAlreadyTranslated = sourceAlreadyTranslatedLocal;
			_rows.AddRange(rows);
			foreach (KeyValuePair<string, string> kv in trBaselineLocal)
			{
				_trBaseline[kv.Key] = kv.Value;
			}
			foreach (string key in knownReferenceRowsLocal)
			{
				_knownReferenceRows.Add(key);
			}
			// Güncellenmiş Türkçe kaynakta genel kaynak-metin belleğini topluca
			// uygulama. Yalnız anahtarı ve kaynak değeri birlikte doğrulanmış
			// approved_updates.tsv kayıtları kullanılır; böylece kısa/çok anlamlı
			// metinler yanlış bağlama taşınmaz.
			if (_tm.Count > 0 && !_sourceAlreadyTranslated)
			{
				Status("Bellek uygulanıyor (" + _tm.Count.ToString("N0") + " kayıt)…");
				tmHit = _tm.ApplyToRows(_rows);
			}
			approvedHit = _approved.ApplyToRows(_rows);
			_approvedApplied = approvedHit;
			int curatedUiHit = ApplyCuratedUiCorrections(_rows);
			ApplyFilter();
			int num = CountNeedGpu();
			string text = (string.IsNullOrEmpty(trPackagePath) ? "—" : Path.GetFileName(Path.GetDirectoryName(trPackagePath)));
			if (_sourceAlreadyTranslated)
			{
				Program.Log($"LoadDat source-authoritative TurkishRows={_sourceTurkishRows} path={_datPath}");
				Status($"Güncellenmiş Türkçe DAT taban alındı · Türkçe sinyalli satır={_sourceTurkishRows:N0} · onaylı +{approvedHit:N0} · bellek +{tmHit:N0} · arayüz düzeltme={curatedUiHit:N0} · kalan ~{num:N0}");
				SetBanner("GÜNCELLENMİŞ TÜRKÇE DAT  ·  kaynak korunuyor  ·  bellek " + tmHit.ToString("N0"));
			}
			else if (trHit > 0)
			{
				SetBanner("DAT LİSTELENDİ  ·  " + _rows.Count.ToString("N0") + " satır  ·  TR paket " + trHit.ToString("N0") + ((tmHit > 0) ? ("  ·  bellek " + tmHit.ToString("N0")) : ""));
				Status($"DAT · {_rows.Count:N0} · TR paket={trHit:N0} ({text}) · bellek +{tmHit:N0} · kalan ~{num:N0}");
				Stat($"TR paket: {trHit:N0}  ·  bellek: {tmHit:N0}");
			}
			else if (tmHit > 0)
			{
				SetBanner("DAT LİSTELENDİ  ·  " + _rows.Count.ToString("N0") + " satır  ·  bellek " + tmHit.ToString("N0") + " uygulandı");
				Status($"DAT · {_rows.Count:N0} satır · bellekten {tmHit:N0} · kalan ~{num:N0} · TM {_tm.Count:N0}");
				Stat($"Bellek: {tmHit:N0} satır doldu");
			}
			else
			{
				SetBanner("DAT LİSTELENDİ  ·  " + _rows.Count.ToString("N0") + " satır");
				Status($"DAT LİSTELENDİ · {_rows.Count:N0} satır · alt={localizationSubfiles:N0} · TM {_tm.Count:N0} · çevrilecek ~{num:N0}");
				Stat("Yükleme tamam (ÇEVRİLMİŞ DAT bulunamadı)");
			}
			RememberLoadedDatStamp();
			Eta("ETA: —");
			if (_bar != null)
			{
				_bar.Value = 0;
				_bar.Maximum = 100;
			}
		}
		catch (Exception ex)
		{
			Program.Log(ex.ToString());
			MessageBox.Show(this, ex.Message, "Yükleme hatası");
		}
		finally
		{
			SetBusy(b: false);
		}
	}

	private static int ApplyCuratedUiCorrections(IList<LocRow> rows)
	{
		if (rows == null || rows.Count == 0)
		{
			return 0;
		}
		Dictionary<string, string[]> rules = new Dictionary<string, string[]>(StringComparer.Ordinal)
		{
			{ "25000023:215:-1:0", new[] { "Kozmetik Kıyafetler [m]Görünümünüzü kişiselleştirmenize olanak tanıyan çeşitli kıyafetlerden oluşan koleksiyon.", "Kozmetik Kıyafetler" } },
			{ "25000023:202:-1:0", new[] { "Zindellik", "Zindelik" } },
			{ "25000023:227:-1:0", new[] { "Zindellik", "Zindelik" } },
			{ "25000023:364:-1:0", new[] { "Hobileri", "Hobi" } }
		};
		int changed = 0;
		foreach (LocRow row in rows)
		{
			if (!rules.TryGetValue(row.Key, out string[] rule))
			{
				continue;
			}
			string current = row.Original ?? "";
			string bad = rule[0];
			string target = rule[1];
			if (string.Equals(current, bad, StringComparison.Ordinal) && !string.Equals(row.Translation ?? "", target, StringComparison.Ordinal))
			{
				row.Translation = target;
				changed++;
			}
		}
		return changed + ManualUiText.Apply(rows);
	}

	private static bool IsAllowedCriticalTranslation(LocRow row)
	{
		return row != null && LocWriteGuard.IsCriticalUiDid(row.Did)
			&& LocWriteGuard.IsSafeCriticalTranslation(row.Original ?? "", row.Translation ?? "");
	}

	private static IDictionary<string, LocWriteGuard.AllowedCriticalRule> AllowedCriticalTargets(IEnumerable<LocRow> rows)
	{
		Dictionary<string, LocWriteGuard.AllowedCriticalRule> result = new Dictionary<string, LocWriteGuard.AllowedCriticalRule>(StringComparer.Ordinal);
		if (rows == null)
		{
			return result;
		}
		foreach (LocRow row in rows)
		{
			if (!IsAllowedCriticalTranslation(row)) continue;
			result[row.Key] = new LocWriteGuard.AllowedCriticalRule
			{
				Source = row.Original ?? "",
				Target = row.Translation ?? ""
			};
		}
		return result;
	}

	private void RememberLoadedDatStamp()
	{
		try
		{
			FileInfo fileInfo = new FileInfo(_datPath);
			_loadedDatLength = fileInfo.Length;
			_loadedDatWriteTimeUtc = fileInfo.LastWriteTimeUtc;
		}
		catch
		{
			_loadedDatLength = -1L;
			_loadedDatWriteTimeUtc = DateTime.MinValue;
		}
	}

	private bool DatChangedSinceLoad()
	{
		try
		{
			if (string.IsNullOrEmpty(_datPath) || !File.Exists(_datPath))
			{
				return true;
			}
			FileInfo fileInfo = new FileInfo(_datPath);
			return fileInfo.Length != _loadedDatLength || fileInfo.LastWriteTimeUtc != _loadedDatWriteTimeUtc;
		}
		catch
		{
			return true;
		}
	}

	private bool DatMatchesStamp(long expectedLength, DateTime expectedWriteTimeUtc)
	{
		try
		{
			if (string.IsNullOrEmpty(_datPath) || !File.Exists(_datPath))
			{
				return false;
			}
			FileInfo fileInfo = new FileInfo(_datPath);
			return fileInfo.Length == expectedLength && fileInfo.LastWriteTimeUtc == expectedWriteTimeUtc;
		}
		catch
		{
			return false;
		}
	}

	private async Task ApplyTxtAsync(bool offerGpu = true, string forcedPath = null)
	{
		if (_rows.Count == 0)
		{
			MessageBox.Show(this, "Önce orijinal DAT seçip «Metinleri yükle» deyin.\r\nSonra «TXT uygula» / «Güncelle TXT+GPU» ile CEVIRILMIS_DAT_TAMAMI.txt seçin.");
			return;
		}
		string path = forcedPath;
		if (string.IsNullOrEmpty(path) || !File.Exists(path))
		{
			using OpenFileDialog openFileDialog = new OpenFileDialog();
			openFileDialog.Filter = "Çeviri TXT|*.txt|All|*.*";
			openFileDialog.Title = "Çeviri TXT (DAT_TAMAMI / ID_TR.tsv / AI dosyası)";
			string text = null;
			if (!string.IsNullOrEmpty(_cfg.LastTxtPath) && File.Exists(_cfg.LastTxtPath))
			{
				text = _cfg.LastTxtPath;
			}
			if (text == null)
			{
				string text2 = ProjPaths.FindImportTxt();
				if (File.Exists(text2))
				{
					text = text2;
				}
			}
			if (text != null)
			{
				openFileDialog.InitialDirectory = Path.GetDirectoryName(text);
				openFileDialog.FileName = Path.GetFileName(text);
			}
			else if (Directory.Exists(ProjPaths.ExportDirPrimary))
			{
				openFileDialog.InitialDirectory = ProjPaths.ExportDirPrimary;
			}
			if (openFileDialog.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			path = openFileDialog.FileName;
			_cfg.LastTxtPath = path;
			_cfg.Save();
		}
		else
		{
			_cfg.LastTxtPath = path;
			try
			{
				_cfg.Save();
			}
			catch
			{
			}
		}
		SetBusy(b: true);
		try
		{
			int applied = 0;
			int keepAsIs = 0;
			int noMap = 0;
			int tmN = 0;
			int mapCount = 0;
			await Task.Run(delegate
			{
				Dictionary<string, string> enToTr;
				Dictionary<string, string> dictionary = DumpTxtImport.LoadMaps(path, delegate(string s)
				{
					try
					{
						if (base.IsHandleCreated && !base.IsDisposed)
						{
							BeginInvoke((Action)delegate
							{
								Status(s);
								Stat(s);
							});
						}
					}
					catch
					{
					}
				}, out enToTr);
				mapCount = dictionary.Count;
				for (int num2 = 0; num2 < _rows.Count; num2++)
				{
					LocRow locRow = _rows[num2];
					string key = locRow.Key;
					if ((!dictionary.TryGetValue(key, out var value) || string.IsNullOrEmpty(value)) && (enToTr == null || !enToTr.TryGetValue(locRow.Original ?? "", out value) || string.IsNullOrEmpty(value)))
					{
						noMap++;
					}
					else if (TextGuard.ShouldKeepAsIs(locRow.Original))
					{
						if (!string.IsNullOrEmpty(value))
						{
							locRow.Translation = value;
						}
						else
						{
							locRow.Translation = locRow.Original;
						}
						keepAsIs++;
						applied++;
					}
					else
					{
						string text3 = TextGuard.Sanitize(locRow.Original, value, allowCompact: false);
						if (string.IsNullOrEmpty(text3) || (string.Equals(text3, locRow.Original, StringComparison.Ordinal) && !string.Equals(value, locRow.Original, StringComparison.Ordinal) && !string.IsNullOrEmpty(value)))
						{
							text3 = value;
						}
						locRow.Translation = text3;
						applied++;
						if (_tm != null && text3 != null && !string.Equals(text3, locRow.Original, StringComparison.Ordinal))
						{
							_tm.Upsert(locRow.Original, text3);
							tmN++;
						}
					}
				}
				try
				{
					_tm.ForceSave();
				}
				catch
				{
				}
			}).ConfigureAwait(continueOnCapturedContext: true);
			_grid.Invalidate();
			int num = CountNeedGpu();
			Status($"TXT uygulandı · map {mapCount:N0} · yazılan {applied:N0} · kalan GPU ~{num:N0} · TM {_tm.Count:N0}");
			Stat("TXT bitti");
			SetBanner($"TXT UYGULANDI  ·  {applied:N0}  ·  kalan ~{num:N0}");
			if (offerGpu && num > 0)
			{
				if (MessageBox.Show(this, "TXT → DAT eşleşmesi bitti.\r\n\r\nTXT kaydı: " + mapCount.ToString("N0") + "\r\nDoldurulan: " + applied.ToString("N0") + "\r\nKalan (yeni / eşleşmeyen): ~" + num.ToString("N0") + "\r\n\r\nKalanları şimdi yerel GPU kalite çevirisiyle dolduralım mı?", "TXT + GPU", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
				{
					SetBusy(b: false);
					await TranslateGpuAsync().ConfigureAwait(continueOnCapturedContext: true);
				}
			}
			else if (offerGpu)
			{
				MessageBox.Show(this, "TXT uygulandı.\r\nDoldurulan: " + applied.ToString("N0") + "\r\nKalan: ~" + num.ToString("N0"), "TXT uygulandı", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			}
		}
		catch (Exception ex)
		{
			Program.Log(ex.ToString());
			MessageBox.Show(this, ex.Message, "TXT hatası");
		}
		finally
		{
			SetBusy(b: false);
		}
	}

	private async Task UpdateFromTxtThenGpuAsync()
	{
		if (_rows.Count == 0)
		{
			MessageBox.Show(this, "Önce yeni/orijinal DAT yükleyin, sonra «Güncelle TXT+GPU».");
			return;
		}
		string text = null;
		if (!string.IsNullOrEmpty(_cfg.LastTxtPath) && File.Exists(_cfg.LastTxtPath))
		{
			text = _cfg.LastTxtPath;
		}
		if (text == null)
		{
			string text2 = ProjPaths.FindImportTxt();
			if (File.Exists(text2))
			{
				text = text2;
			}
		}
		if (text == null || !File.Exists(text))
		{
			using OpenFileDialog openFileDialog = new OpenFileDialog();
			openFileDialog.Filter = "Çeviri TXT|CEVIRILMIS_DAT_TAMAMI.txt;*.txt|All|*.*";
			openFileDialog.Title = "Önceki tam çeviri TXT seç";
			openFileDialog.InitialDirectory = ProjPaths.ExportDirPrimary;
			openFileDialog.FileName = ProjPaths.TranslationMemoryTxtName;
			if (openFileDialog.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			text = openFileDialog.FileName;
		}
		await ApplyTxtAsync(offerGpu: false, text).ConfigureAwait(continueOnCapturedContext: true);
		int num = CountNeedGpu();
		if (num <= 0)
		{
			MessageBox.Show(this, "TXT ile hepsi doldu (veya çevrilecek metin yok).\r\nŞimdi DAT yazabilirsiniz.\r\nÇeviri TXT de klasöre yazılacak.", "Güncelleme bitti");
			await ExportTranslationTxtAsync(silent: false).ConfigureAwait(continueOnCapturedContext: true);
		}
		else
		{
			SetBanner("Güncelleme · kalan GPU ~" + num.ToString("N0"), Color.FromArgb(90, 70, 140));
			Status($"TXT uygulandı · kalan ~{num:N0} → GPU kalite çevirisi…");
			await TranslateGpuAsync().ConfigureAwait(continueOnCapturedContext: true);
		}
	}

	private int CountNeedGpu()
	{
		int num = 0;
		for (int i = 0; i < _rows.Count; i++)
		{
			LocRow locRow = _rows[i];
			if (NeedsGpuTranslation(locRow))
			{
				num++;
			}
		}
		return num;
	}

	private bool NeedsGpuTranslation(LocRow row)
	{
		if (row == null || string.IsNullOrWhiteSpace(row.Original) || row.Original.Length < 2)
		{
			return false;
		}
		if (!string.Equals(row.Translation ?? "", row.Original ?? "", StringComparison.Ordinal))
		{
			return false;
		}
		if (TextGuard.ShouldKeepAsIs(row.Original) || _knownReferenceRows.Contains(row.Key))
		{
			return false;
		}
		return !TranslationQuality.LooksTurkishText(row.Original) && TranslationQuality.LooksEnglishText(row.Original);
	}

	private async Task<string> ExportTranslationTxtAsync(bool silent)
	{
		if (_rows.Count == 0)
		{
			return null;
		}
		CommitLiveTranslation();
		string primary = null;
		string primaryAi = null;
		int written = 0;
		int pendingAi = 0;
		List<string> paths = new List<string>();
		List<string> aiPaths = new List<string>();
		try
		{
			await Task.Run(delegate
			{
				string[] array = ProjPaths.ExportTxtDirs();
				for (int i = 0; i < array.Length; i++)
				{
					string text2 = Path.Combine(array[i], ProjPaths.TranslationMemoryTxtName);
					string text3 = Path.Combine(array[i], ProjPaths.AiPendingTxtName);
					try
					{
						DumpTxtImport.ExportPairResult exportPairResult = DumpTxtImport.ExportPair(text2, text3, _rows, _datPath, delegate(string s)
						{
							try
							{
								if (base.IsHandleCreated && !base.IsDisposed)
								{
									BeginInvoke((Action)delegate
									{
										Status(s);
									});
								}
							}
							catch
							{
							}
						});
						written = Math.Max(written, exportPairResult.TotalRows);
						pendingAi = Math.Max(pendingAi, exportPairResult.PendingForAi);
						paths.Add(text2);
						aiPaths.Add(text3);
						if (primary == null)
						{
							primary = text2;
							primaryAi = text3;
						}
					}
					catch (Exception ex2)
					{
						Program.Log("txt export " + text2 + ": " + ex2.Message);
					}
				}
			}).ConfigureAwait(continueOnCapturedContext: true);
		}
		catch (Exception ex)
		{
			Program.Log(ex.ToString());
			if (!silent)
			{
				MessageBox.Show(this, ex.Message, "TXT dışa aktarma");
			}
			return null;
		}
		if (primary != null)
		{
			_cfg.LastTxtPath = primary;
			try
			{
				_cfg.Save();
			}
			catch
			{
			}
			Status($"TXT · tam {written:N0} · AI kalan {pendingAi:N0} → {primary}");
			Stat("TXT kaydedildi (tam + AI)");
			if (!silent)
			{
				SetBanner($"TXT  ·  {written:N0} satır  ·  AI kalan {pendingAi:N0}");
				string text = ((paths.Count > 1) ? ("\r\n\r\nKopyalar:\r\n" + string.Join("\r\n", paths.ToArray())) : "");
				MessageBox.Show(this, "2 dosya yazıldı:\r\n\r\n1) TAM BELLEK (" + written.ToString("N0") + " satır):\r\n" + primary + "\r\n\r\n2) YAPAY ZEKA İŞİ (" + pendingAi.ToString("N0") + " kalan):\r\n" + primaryAi + "\r\n\r\nAI dosyasını ChatGPT/Claude/Cursor'a ver → TUR satırlarını çevirsin →\r\n«TXT uygula» ile programa al → elle düzelt → «DAT yaz».\r\nDAT otomatik yazılmaz." + text, "Çeviri TXT + AI");
			}
		}
		return primary;
	}

	private static string FormatEta(TimeSpan t)
	{
		if (t.TotalSeconds < 0.0 || double.IsNaN(t.TotalSeconds) || double.IsInfinity(t.TotalSeconds))
		{
			return "—";
		}
		if (t.TotalSeconds < 1.0)
		{
			return "0:00:00";
		}
		int num = (int)t.TotalHours;
		return $"{num}:{t:mm\\:ss}";
	}

	private static string FormatElapsed(TimeSpan t)
	{
		int num = (int)t.TotalHours;
		if (num > 0)
		{
			return $"{num}:{t:mm\\:ss}";
		}
		return t.ToString("mm\\:ss");
	}

	private async Task TranslateGpuAsync()
	{
		if (_rows.Count == 0)
		{
			MessageBox.Show(this, "Önce Dosya Seç ile DAT dosyasını yükleyin.");
			return;
		}
		CommitLiveTranslation();
		PurgeCorruptTranslations();
		var work = new Dictionary<string, List<int>>(StringComparer.Ordinal);
		for (int i = 0; i < _rows.Count; i++)
		{
			LocRow row = _rows[i];
			if (NeedsGpuTranslation(row))
			{
				List<int> indexes;
				if (!work.TryGetValue(row.Original, out indexes)) work[row.Original] = indexes = new List<int>();
				indexes.Add(i);
			}
		}
		var texts = work.Keys.ToList();
		var protectedNames = new HashSet<string>(LotroGpuContext.ProtectedNames, StringComparer.Ordinal);
		for (int i = 0; i < _rows.Count; i++)
		{
			string candidate = (_rows[i].Original ?? "").Trim();
			if (TextGuard.IsLikelyProperName(candidate))
			{
				protectedNames.Add(candidate);
			}
		}
		if (texts.Count == 0)
		{
			MessageBox.Show(this, "GPU ile çevrilecek yeni metin yok.", "GPU Çeviri", MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}
		string runtime = GpuTranslator.FindRuntime();
		if (runtime == null)
		{
			if (MessageBox.Show(this, "GPU kalite bileşenleri bulunamadı. Program Python, NVIDIA PyTorch, hızlı OPUS ve Qwen3-8B kalite modelini otomatik hazırlasın mı?\r\n\r\nİlk kurulum ve ilk model indirmesi yaklaşık 8-15 GB alan kullanabilir.", "GPU bileşenlerini kur", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
		}
		if (MessageBox.Show(this, texts.Count.ToString("N0") + " benzersiz metin RTX ekran kartıyla çevrilecek. OPUS hızlı taslak üretecek; yalnız kalite kontrolünden geçmeyen satırları Qwen3-8B düzeltecek. LOTRO adları, sayılar ve biçim kodları korunacak. Devam edilsin mi?", "GPU kalite çevirisi", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
		_cts = new CancellationTokenSource();
		SetBusy(true);
		SetBanner("GPU çevirisi hazırlanıyor…", Color.FromArgb(50, 70, 140));
		_bar.Maximum = Math.Max(1, texts.Count); _bar.Value = 0;
		try
		{
			CancellationToken ct = _cts.Token;
			Action<string> setupStatus = s => { try { BeginInvoke((Action)(() => Status(s))); } catch { } };
			if (runtime == null) runtime = await GpuTranslator.InstallRuntimeAsync(setupStatus, ct).ConfigureAwait(true);
			string detected = runtime;
			Status("GPU ortamı bulundu: " + detected);
			var results = await GpuTranslator.TranslateAsync(texts, protectedNames.ToList(), detected, (done,total,message) =>
			{
				try { BeginInvoke((Action)(() => { if (done > 0) { _bar.Maximum=Math.Max(1,total);_bar.Value=Math.Min(_bar.Maximum,done);Eta("GPU · "+done.ToString("N0")+"/"+total.ToString("N0")); } else Status(message.Replace('|',' ')); })); } catch { }
			}, ct).ConfigureAwait(true);
			int saved=0,rejected=0;
			foreach (GpuTranslator.Result result in results)
			{
				if (result.Index < 0 || result.Index >= texts.Count) { rejected++; continue; }
				string en=texts[result.Index];
				string tr=TextGuard.Sanitize(en,result.Translation ?? en,allowCompact:false);
				if (!string.IsNullOrEmpty(result.QualityProblem) || string.IsNullOrWhiteSpace(tr) || string.Equals(en,tr,StringComparison.Ordinal) || TranslationQuality.LooksCorruptPair(en,tr) || !TextGuard.HasSameProtectedTokens(en, tr))
				{
					rejected++;
					if (!string.IsNullOrEmpty(result.QualityProblem)) Program.Log("GPU RED " + en.Substring(0, Math.Min(80, en.Length)) + " :: " + result.QualityProblem);
					continue;
				}
				_tm.Upsert(en,tr);
				List<int> indexes;
				if (work.TryGetValue(en,out indexes)) foreach(int index in indexes) _rows[index].Translation=tr;
				saved++;
			}
			_tm.ForceSave();
			_grid.Invalidate();
			BindEditor(_boundRow);
			SetBanner("GPU ÇEVİRİ BİTTİ · kaydedilen " + saved.ToString("N0"), Color.FromArgb(30,110,55));
			Status("GPU tamamlandı · Kaydedilen " + saved.ToString("N0") + " · Güvenlik nedeniyle atlanan " + rejected.ToString("N0"));
			Eta("GPU tamamlandı");
			MessageBox.Show(this, "GPU çevirisi tamamlandı.\r\n\r\nKaydedilen: " + saved.ToString("N0") + "\r\nGüvenlik kontrolünde atlanan: " + rejected.ToString("N0"), "GPU Çeviri", MessageBoxButtons.OK, MessageBoxIcon.Information);
		}
		catch (OperationCanceledException) { SetBanner("GPU çevirisi durduruldu.", Color.DarkOrange); }
		catch (Exception ex) { Program.Log("GPU ERROR " + ex); MessageBox.Show(this, ex.Message, "GPU çeviri hatası", MessageBoxButtons.OK, MessageBoxIcon.Error); SetBanner("GPU çevirisi tamamlanamadı.", Color.DarkRed); }
		finally { SetBusy(false); }
	}

	private void RefreshWorkerLabelSafe()
	{
		try
		{
			if (base.IsHandleCreated && !base.IsDisposed)
			{
				BeginInvoke(new Action(RefreshWorkerLabel));
			}
		}
		catch
		{
		}
	}

	private async Task SaveDatAsync(bool silent = false)
	{
		if (_rows.Count == 0 || string.IsNullOrEmpty(_datPath))
		{
			if (silent) throw new InvalidOperationException("Birleştirme için kaynak DAT yüklenmedi.");
			MessageBox.Show(this, "Önce Dosya Seç ile client_local_English.dat yükleyin.");
			return;
		}
		if (!DatExportSession.IsProcessX86)
		{
			if (silent) throw new InvalidOperationException("datexport.dll için x86 işlem gerekli.");
			MessageBox.Show(this, "datexport.dll 32-bit. Freedom klasöründeki programı (x86) kullanın.", "x86 gerekli", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return;
		}
		if (DatChangedSinceLoad())
		{
			const string changedMessage = "Kaynak DAT, metinler yüklendikten sonra değişti. Eski satırların güncellenmiş dosyaya yazılmasını önlemek için DAT'ı yeniden yükleyip işlemi tekrar başlatın.";
			if (silent) throw new InvalidOperationException(changedMessage);
			MessageBox.Show(this, changedMessage, "Kaynak DAT değişti", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return;
		}
		CommitLiveTranslation();
		int tmUpdated = 0;
		try
		{
			tmUpdated = _tm.CaptureFromRowsAndSave(_rows);
			Status($"TM güncellendi · +{tmUpdated:N0} · toplam {_tm.Count:N0} → {_tm.PathFile}");
			Program.Log($"DAT yaz öncesi TM upsert={tmUpdated} total={_tm.Count} {_tm.PathFile}");
		}
		catch (Exception ex)
		{
			try
			{
				Program.Log("TM save fail: " + ex.Message);
			}
			catch
			{
			}
		}
		string desk = ProjPaths.OutDatDir;
		string reportDir = Program.AppDir;
		string fileName = Path.GetFileName(_datPath);
		if (string.IsNullOrEmpty(fileName))
		{
			fileName = "client_local_English.dat";
		}
		string dest = Path.Combine(desk, fileName);
		string buildingPath = Path.Combine(reportDir, fileName + ".building");
		try
		{
			_cfg.LastOutFolder = desk;
			_cfg.Save();
		}
		catch
		{
		}
		if (string.Equals(Path.GetFullPath(dest), Path.GetFullPath(_datPath), StringComparison.OrdinalIgnoreCase))
		{
			if (!silent && MessageBox.Show(this, "Kaynak DAT zaten çıktı klasöründe; üzerine yazılacak.\r\nDevam?", "Uyarı", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
			{
				return;
			}
		}
		else if (File.Exists(dest) && !silent && MessageBox.Show(this, "Çıktı klasöründe aynı isimde dosya var. Üzerine yazılsın mı?\r\n" + dest, "Kaydet", MessageBoxButtons.YesNo) != DialogResult.Yes)
		{
			return;
		}
		SetBusy(b: true);
		SetBanner("DAT yazılıyor…", Color.FromArgb(40, 80, 120));
		Eta("ETA hesaplanıyor…");
		try
		{
			int okN = 0;
			int failN = 0;
			int sameN = 0;
			int dirtyRows = 0;
			int skipFlat = 0;
			int transplantN = 0;
			string writeMode = "hybrid";
			_ = _datPath;
			Stopwatch writeSw = Stopwatch.StartNew();
			string trDatPath = null;
			try
			{
				trDatPath = ProjPaths.FindTrDat();
			}
			catch
			{
				trDatPath = null;
			}
			string enBase = _datPath;
			try
			{
				string text3 = ProjPaths.FindEnDat();
				if (!string.IsNullOrEmpty(text3) && File.Exists(text3))
				{
					enBase = text3;
				}
			}
			catch
			{
			}
			if (!string.IsNullOrEmpty(trDatPath) && File.Exists(trDatPath) && string.Equals(Path.GetFullPath(enBase), Path.GetFullPath(trDatPath), StringComparison.OrdinalIgnoreCase))
			{
				enBase = _datPath;
			}
			string enBaseUsed = enBase;
			// Eski TR DAT yalnız yükleme sırasında çeviri referansı olarak kullanılır.
			// Çıktı daima güncel/seçili EN DAT üzerine anahtar bazında kurulur; böylece
			// oyun güncellemesindeki yeni alt dosyalar eski paketle ezilmez.
			bool num = false;
			if (_bar != null)
			{
				_bar.Maximum = 100;
				_bar.Value = 0;
			}
			if (num)
			{
				writeMode = "LocTransplant";
				Eta("TR transplant…");
				SetBanner("DAT transplant (TR→EN)…", Color.FromArgb(40, 80, 120));
				string failLog = Path.Combine(reportDir, "EnTrApply_fail.txt");
				await Task.Run(delegate
				{
					Action<string> progress = delegate(string s)
					{
						try
						{
							if (base.IsHandleCreated && !base.IsDisposed)
							{
								BeginInvoke((Action)delegate
								{
									Status(s);
									Stat(s);
									Eta(s);
									SetBanner(s, Color.FromArgb(40, 80, 120));
									if (_bar != null && s != null && s.StartsWith("Transplant ", StringComparison.Ordinal))
									{
										try
										{
											int num3 = s.IndexOf('/');
											int num4 = s.IndexOf(' ', 11);
											if (num3 > 0 && num4 > 0 && num4 < num3)
											{
												string s2 = s.Substring(11, num4 - 11).Replace(".", "").Replace(",", "");
												string text6 = s.Substring(num3 + 1);
												int num5 = text6.IndexOf(' ');
												if (num5 < 0)
												{
													num5 = text6.IndexOf('·');
												}
												if (num5 > 0)
												{
													text6 = text6.Substring(0, num5);
												}
												text6 = text6.Replace(".", "").Replace(",", "").Trim();
												if (int.TryParse(s2, out var result2) && int.TryParse(text6, out var result3) && result3 > 0)
												{
													_bar.Value = Math.Min(100, (int)(100.0 * (double)result2 / (double)result3));
												}
											}
										}
										catch
										{
										}
									}
								});
							}
						}
						catch
						{
						}
					};
					LocTransplant.Result result = LocTransplant.Apply(enBase, trDatPath, dest, progress, failLog);
					okN = result.Ok;
					sameN = result.Same;
					failN = result.Fail;
					transplantN = result.Ok;
					skipFlat = result.SkipNoEn;
					writeMode = (string.IsNullOrEmpty(result.Mode) ? "LocTransplant" : result.Mode);
					try
					{
						File.WriteAllText(Path.Combine(reportDir, "YAZIM_RAPOR.txt"), $"mode={writeMode}\r\nOK={result.Ok}\r\nfail={result.Fail}\r\nsame_did={result.Same}\r\nskip_no_en={result.SkipNoEn}\r\ntransplant={result.Ok}\r\n250001AF={result.TokenTableWritten}\r\nsize={result.OutSize}\r\ntr={trDatPath}\r\nbase={enBase}\r\n{dest}\r\nsure={result.Elapsed}\r\nfail_log={failLog}\r\n", Encoding.UTF8);
					}
					catch
					{
					}
					Status($"Transplant mode={writeMode} OK={result.Ok} fail={result.Fail} → {dest}");
					try
					{
						Program.Log($"save LocTransplant mode={writeMode} ok={result.Ok} fail={result.Fail} af={result.TokenTableWritten} size={result.OutSize} {dest}");
					}
					catch
					{
					}
				});
				// after TrFullCopy: overlay dirty session rows
				int overlayOk = 0;
				int overlayFail = 0;
				int overlaySame = 0;
				int overlayDirtyRows = 0;
				int overlaySkipFlat = 0;
				int overlayDirtyDids = 0;
				try
				{
					if (File.Exists(dest))
					{
						Eta("Session overlay...");
						SetBanner("DAT session overlay...", Color.FromArgb(40, 80, 120));
						await Task.Run(delegate
						{
							Action<string> ovProgress = delegate(string msg)
							{
								try
								{
									if (base.IsHandleCreated && !base.IsDisposed)
									{
										BeginInvoke((Action)delegate
										{
											Status(msg);
											Stat(msg);
											Eta(msg);
										});
									}
								}
								catch
								{
								}
							};
							OverlayResult ov = OverlaySessionEditsOntoDat(dest, ovProgress);
							overlayOk = ov.Ok;
							overlayFail = ov.Fail;
							overlaySame = ov.Same;
							overlayDirtyRows = ov.DirtyRows;
							overlaySkipFlat = ov.SkipFlat;
							overlayDirtyDids = ov.DirtyDids;
						});
						if (overlayDirtyDids > 0 || overlayDirtyRows > 0)
						{
							writeMode = writeMode + "+SessionOverlay";
						}
						transplantN = okN;
						try
						{
							File.WriteAllText(Path.Combine(reportDir, "YAZIM_RAPOR.txt"), $"mode={writeMode}\r\nOK={okN}\r\noverlay_ok={overlayOk}\r\noverlay_fail={overlayFail}\r\noverlay_same={overlaySame}\r\noverlay_dirty_did={overlayDirtyDids}\r\noverlay_dirty_satir={overlayDirtyRows}\r\noverlay_skip_flat={overlaySkipFlat}\r\nfail={failN + overlayFail}\r\nsame_did={sameN}\r\nskip_no_en={skipFlat}\r\ntransplant={okN}\r\nsize={(File.Exists(dest) ? new FileInfo(dest).Length : 0)}\r\ntr={trDatPath}\r\nbase={enBase}\r\n{dest}\r\nsure={writeSw.Elapsed}\r\nfail_log={failLog}\r\n", Encoding.UTF8);
						}
						catch
						{
						}
						Status($"Transplant+overlay mode={writeMode} overlayDid={overlayDirtyDids} dirtyRow={overlayDirtyRows} ovOK={overlayOk} ovFail={overlayFail}");
						Program.Log($"save overlay dirtyDid={overlayDirtyDids} dirtyRow={overlayDirtyRows} ok={overlayOk} fail={overlayFail} skipFlat={overlaySkipFlat} {dest}");
					}
				}
				catch (Exception exOv)
				{
					try
					{
						Program.Log("session overlay fail: " + exOv);
					}
					catch
					{
					}
					throw;
				}
			}
			else
			{
				writeMode = "datexport-native-safe";
				Dictionary<int, List<LocRow>> byDidPre = (from r in _rows
					group r by r.Did).ToDictionary((IGrouping<int, LocRow> g) => g.Key, (IGrouping<int, LocRow> g) => (from r in g
					orderby r.RecordIndex, r.GroupIndex, r.IndexInGroup
					select r).ToList());
				int num2 = 0;
				foreach (KeyValuePair<int, List<LocRow>> item in byDidPre)
				{
					if ((int)((uint)item.Key >> 24) != 37 || (LocWriteGuard.IsCriticalUiDid(item.Key) && !item.Value.Any(IsAllowedCriticalTranslation)))
					{
						continue;
					}
					bool flag = false;
					foreach (LocRow item2 in item.Value)
					{
						if (!string.Equals(item2.Translation, item2.Original, StringComparison.Ordinal))
						{
							flag = true;
							break;
						}
					}
					if (flag)
					{
						num2++;
					}
				}
				int workTotal = Math.Max(1, num2);
				if (_bar != null)
				{
					_bar.Maximum = workTotal;
					_bar.Value = 0;
				}
				Eta($"ETA hesaplanıyor…  ·  0/{workTotal:N0} alt dosya");
				string containerBase = _datPath;
				// ÇEVRİLMİŞ DAT yalnızca metin referansıdır. Kapsayıcı olarak daima
				// kullanıcının seçtiği güncel DAT'ı kopyala; aksi halde eşit boyutlu
				// eski bir yama güncellemenin alt dosyalarını sessizce geri alabilir.
				bool useContainer = false;
				enBaseUsed = containerBase;
				LocWriteGuard.CriticalMigrationResult criticalMigration = null;
				await Task.Run(delegate
				{
					int processed = 0;
					DateTime started = DateTime.UtcNow;
					Action action = delegate
					{
						int p = processed;
						double num7 = Math.Max(0.001, (DateTime.UtcNow - started).TotalSeconds);
						double num8 = ((p >= 2 && num7 >= 0.3) ? ((double)p / num7) : 0.0);
						int num9 = workTotal - p;
						TimeSpan t = ((num8 > 0.05) ? TimeSpan.FromSeconds((double)num9 / num8) : TimeSpan.Zero);
						string line = ((num8 > 0.05) ? $"ETA {FormatEta(t)}  ·  {num8:0.0}/sn  ·  {p:N0}/{workTotal:N0}  ·  OK={okN} fail={failN}" : $"ETA…  ·  {p:N0}/{workTotal:N0}  ·  OK={okN} fail={failN}");
						try
						{
							if (base.IsHandleCreated && !base.IsDisposed)
							{
								BeginInvoke((Action)delegate
								{
									if (_bar != null)
									{
										_bar.Value = Math.Min(_bar.Maximum, p);
									}
									Eta(line);
									SetBanner("DAT yazılıyor…  " + p.ToString("N0") + "/" + workTotal.ToString("N0"), Color.FromArgb(40, 80, 120));
								});
							}
						}
						catch
						{
						}
					};
					Action<string> action2 = delegate(string s)
					{
						Status(s);
						Stat(s);
					};
					action2("Kopyalanıyor…");
					string text6 = buildingPath;
					if (File.Exists(text6))
					{
						File.Delete(text6);
					}
					File.Copy(containerBase, text6, overwrite: true);
					// Kritik grammar/string-table payload'ları eski bir DAT'tan bütün olarak
					// taşınmaz. Topoloji aynı görünse bile sentinel metinleri güncellenmiş
					// olabilir; mevcut kaynak payload'ı korunur.
					if (useContainer)
					{
						action2("Resmî güncelleme + Türkçe kap birleştiriliyor…");
						NativeContainerResult merged = MergeIntoNativeContainer(_datPath, text6, byDidPre, delegate(int done, int total)
						{
							processed = Math.Min(workTotal, (int)((long)done * workTotal / Math.Max(1, total)));
							action();
						});
						okN = merged.Ok;
						sameN = merged.Same;
						failN = merged.Fail;
						dirtyRows = merged.DirtyRows;
					}
					else
					using (DatExportSession datExportSession = new DatExportSession())
					{
						datExportSession.Open(text6, writable: true);
						action2("Dizin…");
						Dictionary<int, int[]> dictionary = datExportSession.LoadSizeMap();
						foreach (KeyValuePair<int, List<LocRow>> item3 in byDidPre)
						{
							int key = item3.Key;
							if ((int)((uint)key >> 24) == 37)
							{
								if (LocWriteGuard.IsCriticalUiDid(key) && ((criticalMigration != null && criticalMigration.Transplanted.Contains(key)) || !item3.Value.Any(IsAllowedCriticalTranslation)))
								{
									sameN++;
									continue;
								}
								List<LocRow> value = item3.Value;
								bool flag2 = false;
								for (int num3 = 0; num3 < value.Count; num3++)
								{
									if (!string.Equals(value[num3].Translation, value[num3].Original, StringComparison.Ordinal))
									{
										flag2 = true;
										break;
									}
								}
								int[] value2;
								if (!flag2)
								{
									int num4 = sameN;
									sameN = num4 + 1;
								}
								else if (!dictionary.TryGetValue(key, out value2))
								{
									int num4 = failN;
									failN = num4 + 1;
									processed++;
									action();
								}
								else
								{
									int size = value2[0];
									int iteration = value2[1];
									byte[] array2;
									int version;
									try
									{
										array2 = datExportSession.ReadSubfile(key, size, out version);
									}
									catch
									{
										int num4 = failN;
										failN = num4 + 1;
										processed++;
										action();
										continue;
									}
									// datexport returns the complete uncompressed subfile payload.
									byte[] array3 = array2;
									LocBin locBin;
									try
									{
										locBin = LocBin.Parse(array3, key);
									}
									catch
									{
										int num4 = failN;
										failN = num4 + 1;
										processed++;
										action();
										continue;
									}
									List<LocRow> rows = locBin.GetRows(key);
									if (rows == null || rows.Count == 0)
									{
										int num4 = failN;
										failN = num4 + 1;
										processed++;
										action();
									}
									else
									{
										Dictionary<string, LocRow> dictionary2 = new Dictionary<string, LocRow>(StringComparer.Ordinal);
										foreach (LocRow item4 in value)
										{
											dictionary2[item4.Key] = item4;
										}
										bool flag4 = false;
										for (int num5 = 0; num5 < rows.Count; num5++)
										{
											string text7 = rows[num5].Original ?? "";
											string text8 = text7;
											if (dictionary2.TryGetValue(rows[num5].Key, out var value3)
												&& string.Equals(value3.Original ?? "", text7, StringComparison.Ordinal)
												&& !string.IsNullOrEmpty(value3.Translation))
											{
												text8 = value3.Translation;
											}
											if (text8.Length == 0)
											{
												text8 = text7;
											}
										if (!string.Equals(text8, text7, StringComparison.Ordinal))
										{
											bool approvedTarget = _approved.IsApprovedTarget(rows[num5].Key, text7, text8);
											text8 = ((!TextGuard.ShouldKeepAsIs(text7) || approvedTarget) ? TextGuard.Sanitize(text7, text8, allowCompact: false) : text7);
										}
											rows[num5].Translation = text8;
											if (!string.Equals(text8, text7, StringComparison.Ordinal))
											{
												flag4 = true;
												int num4 = dirtyRows;
												dirtyRows = num4 + 1;
											}
										}
										if (!flag4)
										{
											int num4 = sameN;
											sameN = num4 + 1;
											processed++;
											action();
										}
										else
										{
											byte[] array4 = null;
											if (false && locBin.UsedFlatFallback)
											{
												List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
												for (int num6 = 0; num6 < rows.Count; num6++)
												{
													string text9 = rows[num6].Original ?? "";
													string text10 = rows[num6].Translation ?? text9;
													if (!string.Equals(text9, text10, StringComparison.Ordinal))
													{
														list.Add(new KeyValuePair<string, string>(text9, text10));
													}
												}
												array4 = InPlaceLocPatch.Apply(array3, locBin, list, out var _);
												if (array4 == null)
												{
													int num4 = skipFlat;
													skipFlat = num4 + 1;
													num4 = failN;
													failN = num4 + 1;
													processed++;
													action();
													continue;
												}
											}
											else
											{
												try
												{
													LocWriteGuard.Result result = LocWriteGuard.TryBuildUncompressed(locBin, rows, array2);
											array4 = result?.Blob;
											if (array4 == null && result != null)
											{
												Program.Log("DAT guard red 0x" + key.ToString("X8") + ": " + result.Reason);
											}
												}
												catch
												{
													array4 = null;
												}
												if (array4 == null)
												{
													int num4 = failN;
													failN = num4 + 1;
													processed++;
													action();
													continue;
												}
											}
											try
											{
												if (datExportSession.WriteSubfile(key, array4, version, iteration) < 0)
												{
													int num4 = failN;
													failN = num4 + 1;
												}
												else
												{
													int num4 = okN;
													okN = num4 + 1;
												}
											}
											catch
											{
												int num4 = failN;
												failN = num4 + 1;
											}
											processed++;
											if (processed % 5 == 0 || processed >= workTotal)
											{
												action();
											}
										}
									}
								}
							}
						}
						action2("Flush…");
						datExportSession.Flush();
						datExportSession.Close();
					}
					if (failN != 0)
					{
						throw new InvalidDataException($"DAT yazımında {failN} alt dosya başarısız oldu; mevcut çıktı korunuyor.");
					}
					if (!NativeContainerShapeIsValid(containerBase, text6, out string containerReason))
					{
						throw new InvalidDataException("DAT kapsayıcı doğrulaması: " + containerReason + "; mevcut çıktı korunuyor.");
					}
					ISet<int> migratedCritical = criticalMigration?.Transplanted;
					if (!LocWriteGuard.CriticalUiPayloadsMatchExpected(_datPath, trDatPath, text6, migratedCritical, AllowedCriticalTargets(byDidPre.Values.SelectMany(rows => rows)), out string criticalReason))
					{
						throw new InvalidDataException("STRING TABLE koruması: " + criticalReason + "; mevcut çıktı korunuyor.");
					}
					if (DatChangedSinceLoad())
					{
						throw new IOException("Kaynak DAT yazım sırasında değişti. Geçici çıktı yayımlanmadı; DAT'ı yeniden yükleyin.");
					}
					if (File.Exists(dest))
					{
						File.Replace(text6, dest, null, ignoreMetadataErrors: true);
					}
					else
					{
						File.Move(text6, dest);
					}
					try
					{
						File.WriteAllText(Path.Combine(reportDir, "YAZIM_RAPOR.txt"), $"mode={writeMode}\r\nOK={okN}\r\nfail={failN}\r\nsame_did={sameN}\r\nskip_flat={skipFlat}\r\ntransplant=0\r\ndirty_satir={dirtyRows}\r\ncritical_migrated={criticalMigration?.Transplanted.Count ?? 0}\r\ncritical_compatible={criticalMigration?.Compatible ?? 0}\r\ncritical_skipped={criticalMigration?.Skipped ?? 0}\r\ntr=\r\nbase={_datPath}\r\n{dest}\r\nsure={writeSw.Elapsed}\r\n", Encoding.UTF8);
					}
					catch
					{
					}
					Status($"Yazıldı OK={okN} fail={failN} → {dest}");
					try
					{
						Program.Log($"save Freedom hybrid ok={okN} fail={failN} skipFlat={skipFlat} {dest}");
					}
					catch
					{
					}
				});
			}
			if (!File.Exists(dest))
			{
				throw new FileNotFoundException("DAT çıktısı oluşmadı.", dest);
			}
			long requiredDatSize = (_loadedDatLength >= 0L) ? _loadedDatLength : new FileInfo(enBaseUsed).Length;
			long writtenDatSize = new FileInfo(dest).Length;
			if (!NativeContainerShapeIsValid(enBaseUsed, dest, out string writtenReason))
			{
				throw new InvalidDataException("Yazılan DAT kapsayıcı doğrulaması: " + writtenReason);
			}
			try
			{
				File.AppendAllText(Path.Combine(reportDir, "YAZIM_RAPOR.txt"), $"source_size={requiredDatSize}\r\noutput_size={writtenDatSize}\r\nsize_growth={writtenDatSize - requiredDatSize}\r\ncontainer_shape_valid=true\r\n", Encoding.UTF8);
			}
			catch
			{
			}
			if (_bar != null)
			{
				_bar.Value = _bar.Maximum;
			}
			Eta("ETA: 0:00:00 · yazma bitti");
			try
			{
				int val = _tm.CaptureFromRowsAndSave(_rows);
				tmUpdated = Math.Max(tmUpdated, val);
			}
			catch
			{
			}
			SetBanner("DAT YAZILDI  ·  CIKTI\\" + fileName + "  ·  " + writeMode + "  ·  TM " + _tm.Count.ToString("N0"));
			string path2 = Path.Combine(reportDir, "YAZIM_RAPOR.txt");
			string text5 = (File.Exists(path2) ? ("\r\n\r\n" + File.ReadAllText(path2)) : "");
			if (!silent) MessageBox.Show(this, "DAT çıktı klasörüne yazıldı:\r\n" + dest + "\r\nBoyut: " + new FileInfo(dest).Length.ToString("N0") + "\r\nSüre: " + FormatElapsed(writeSw.Elapsed) + "\r\nMod: " + writeMode + "\r\nBase: " + enBaseUsed + "\r\n\r\nÇeviri belleği güncellendi (tm.tsv):\r\n" + _tm.PathFile + "\r\nKayıt: " + _tm.Count.ToString("N0") + "  ·  bu yazımda değişen ≈ " + tmUpdated.ToString("N0") + text5, "Başarılı");
		}
		catch (Exception ex2)
		{
			try
			{
				if (File.Exists(buildingPath)) File.Delete(buildingPath);
			}
			catch
			{
			}
			Program.Log(ex2.ToString());
			if (silent) throw;
			MessageBox.Show(this, ex2.Message, "Kayıt hatası");
		}
		finally
		{
			try
			{
				if (File.Exists(buildingPath)) File.Delete(buildingPath);
			}
			catch
			{
			}
			SetBusy(b: false);
		}
	}

	public async Task RunLatestDatMergeAsync(string reportPath)
	{
		await LoadDatAsync().ConfigureAwait(true);
		int total = _rows.Count;
		int keep = _rows.Count(r => TextGuard.ShouldKeepAsIs(r.Original ?? ""));
		int translated = _rows.Count(r => !TextGuard.ShouldKeepAsIs(r.Original ?? "") && !string.Equals(r.Translation ?? "", r.Original ?? "", StringComparison.Ordinal));
		int remaining = _rows.Count(NeedsGpuTranslation);
		int uniqueRemaining = _rows.Where(NeedsGpuTranslation).Select(r => r.Original ?? "").Distinct(StringComparer.Ordinal).Count();
		await SaveDatAsync(silent: true).ConfigureAwait(true);
		string output = Path.Combine(ProjPaths.OutDatDir, Path.GetFileName(_datPath));
		long sourceSize = new FileInfo(_datPath).Length;
		string referencePath = ProjPaths.FindTrDat();
		long outputSize = File.Exists(output) ? new FileInfo(output).Length : 0L;
		if (!NativeContainerShapeIsValid(_datPath, output, out string outputReason))
		{
			throw new InvalidDataException("Son DAT kapsayıcı doğrulaması: " + outputReason);
		}
		string reportDirectory = Path.GetDirectoryName(reportPath);
		if (!string.IsNullOrEmpty(reportDirectory)) Directory.CreateDirectory(reportDirectory);
		File.WriteAllText(reportPath,
			"mode=latest-dat-safe-merge\r\n" +
			"source=" + _datPath + "\r\n" +
			"reference=" + (referencePath ?? "") + "\r\n" +
			"translation_memory=" + _tm.PathFile + "\r\n" +
			"source_already_translated=" + _sourceAlreadyTranslated.ToString().ToLowerInvariant() + "\r\n" +
			"source_turkish_signal_rows=" + _sourceTurkishRows + "\r\n" +
			"approved_rules=" + _approved.Count + "\r\n" +
			"approved_rows_applied=" + _approvedApplied + "\r\n" +
			"rows_total=" + total + "\r\n" +
			"rows_keep_as_is=" + keep + "\r\n" +
			"rows_translated=" + translated + "\r\n" +
			"rows_remaining_english=" + remaining + "\r\n" +
			"unique_remaining_english=" + uniqueRemaining + "\r\n" +
			"output=" + output + "\r\n" +
			"official_source_size=" + sourceSize + "\r\n" +
			"output_size=" + outputSize + "\r\n" +
			"size_growth=" + (outputSize - sourceSize) + "\r\n" +
			"container_shape_valid=true\r\n",
			new UTF8Encoding(true));
	}

	private static bool NativeContainerShapeIsValid(string sourcePath, string candidatePath, out string reason)
	{
		reason = "";
		if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
		{
			reason = "kaynak DAT bulunamadı";
			return false;
		}
		if (string.IsNullOrEmpty(candidatePath) || !File.Exists(candidatePath))
		{
			reason = "aday DAT bulunamadı";
			return false;
		}

		long sourceSize = new FileInfo(sourcePath).Length;
		long candidateSize = new FileInfo(candidatePath).Length;
		long growth = candidateSize - sourceSize;
		if (growth < 0L || growth > 64L * 1024L * 1024L)
		{
			reason = $"beklenmeyen boyut değişimi kaynak={sourceSize}, aday={candidateSize}";
			return false;
		}

		int sourceBlockSize;
		int sourceSubfiles;
		int sourceVnumDat;
		int sourceVnumGame;
		uint sourceDatId;
		string sourceStamp;
		string sourceGuid;
		using (DatExportSession source = new DatExportSession())
		{
			source.Open(sourcePath, writable: false);
			sourceBlockSize = source.BlockSize;
			sourceSubfiles = source.NumSubfiles;
			sourceVnumDat = source.VnumDatFile;
			sourceVnumGame = source.VnumGameData;
			sourceDatId = source.DatFileId;
			sourceStamp = source.DatIdStamp;
			sourceGuid = source.FirstIterationGuid;
		}

		if (sourceBlockSize <= 0 || growth % sourceBlockSize != 0L)
		{
			reason = $"boyut artışı DAT blok boyutuyla uyumsuz: artış={growth}, blok={sourceBlockSize}";
			return false;
		}

		using (DatExportSession candidate = new DatExportSession())
		{
			candidate.Open(candidatePath, writable: false);
			if (candidate.BlockSize != sourceBlockSize ||
				candidate.NumSubfiles != sourceSubfiles ||
				candidate.VnumDatFile != sourceVnumDat ||
				candidate.VnumGameData != sourceVnumGame ||
				candidate.DatFileId != sourceDatId ||
				!string.Equals(candidate.DatIdStamp, sourceStamp, StringComparison.Ordinal) ||
				!string.Equals(candidate.FirstIterationGuid, sourceGuid, StringComparison.Ordinal))
			{
				reason = "DAT başlığı veya alt dosya kataloğu kaynakla eşleşmiyor";
				return false;
			}
		}
		return true;
	}

	private sealed class NativeContainerResult
	{
		public int Ok;
		public int Same;
		public int Fail;
		public int DirtyRows;
		public int Total;
	}

	private static NativeContainerResult MergeIntoNativeContainer(string officialPath, string buildingPath, Dictionary<int, List<LocRow>> requestedRows, Action<int, int> progress)
	{
		NativeContainerResult result = new NativeContainerResult();
		using (DatExportSession source = new DatExportSession(1))
		using (DatExportSession output = new DatExportSession(0))
		{
			source.Open(officialPath, writable: false);
			output.Open(buildingPath, writable: true);
			Dictionary<int, int[]> sourceMap = source.LoadSizeMap();
			Dictionary<int, int[]> outputMap = output.LoadSizeMap();
			List<int> dids = sourceMap.Keys.Where(did => (uint)did >> 24 == 37).OrderBy(did => unchecked((uint)did)).ToList();
			result.Total = dids.Count;
			for (int index = 0; index < dids.Count; index++)
			{
				int did = dids[index];
				int[] sourceMeta = sourceMap[did];
				if (!outputMap.TryGetValue(did, out int[] outputMeta))
				{
					result.Fail++;
					continue;
				}
				// The translated container's core UI/string-table payloads are a
				// known game-working unit. Replacing the whole payload with the
				// official English version makes the UI English; rebuilding only
				// selected rows can invalidate its internal grammar table. Preserve
				// these subfiles byte-for-byte and update every other localization DID.
				try
				{
					int sourceVersion;
					byte[] officialPayload = source.ReadSubfile(did, sourceMeta[0], out sourceVersion);
					byte[] candidate = officialPayload;
					if (requestedRows.TryGetValue(did, out List<LocRow> wanted) && wanted.Any(row => !string.Equals(row.Translation ?? "", row.Original ?? "", StringComparison.Ordinal)))
					{
						LocBin bin = LocBin.Parse(officialPayload, did);
						List<LocRow> rows = bin.GetRows(did);
						Dictionary<string, LocRow> wantedByKey = wanted.ToDictionary(row => row.Key, row => row, StringComparer.Ordinal);
						int dirty = 0;
						foreach (LocRow row in rows)
						{
							string original = row.Original ?? "";
							string translation = original;
							if (wantedByKey.TryGetValue(row.Key, out LocRow selected)
								&& string.Equals(selected.Original ?? "", original, StringComparison.Ordinal)
								&& !string.IsNullOrEmpty(selected.Translation))
							{
								translation = selected.Translation;
							}
							row.Translation = translation;
							if (!string.Equals(original, translation, StringComparison.Ordinal)) dirty++;
						}
						if (dirty > 0)
						{
							LocWriteGuard.Result built = LocWriteGuard.TryBuildUncompressed(bin, rows, officialPayload);
							if (built == null || (built.Blob == null && !string.Equals(built.Reason, "aynı", StringComparison.Ordinal)))
							{
								throw new InvalidDataException("guard: " + (built?.Reason ?? "sonuç yok"));
							}
							candidate = built.Blob ?? officialPayload;
							result.DirtyRows += dirty;
						}
					}
					int outputVersion;
					byte[] current = output.ReadSubfile(did, outputMeta[0], out outputVersion);
					if (ByteArraysEqual(current, candidate))
					{
						result.Same++;
					}
					else if (output.WriteSubfile(did, candidate, sourceVersion, sourceMeta[1]) < 0)
					{
						result.Fail++;
					}
					else
					{
						result.Ok++;
					}
				}
				catch (Exception ex)
				{
					result.Fail++;
					Program.Log("container merge 0x" + did.ToString("X8") + ": " + ex.Message);
				}
				if (index % 1000 == 0 || index + 1 == dids.Count) progress?.Invoke(index + 1, dids.Count);
			}
			output.Flush();
		}
		return result;
	}

	private static bool ByteArraysEqual(byte[] left, byte[] right)
	{
		if (ReferenceEquals(left, right)) return true;
		if (left == null || right == null || left.Length != right.Length) return false;
		for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
		return true;
	}


	private sealed class OverlayResult
	{
		public int Ok;

		public int Fail;

		public int Same;

		public int DirtyRows;

		public int SkipFlat;

		public int DirtyDids;
	}

	private bool IsSessionDirty(LocRow row)
	{
		if (row == null)
		{
			return false;
		}
		string text = row.Translation ?? "";
		if (_trBaseline.TryGetValue(row.Key, out var value))
		{
			return !string.Equals(text, value ?? "", StringComparison.Ordinal);
		}
		return !string.Equals(text, row.Original ?? "", StringComparison.Ordinal);
	}

	/// <summary>TrFullCopy sonrasi session/manual edits OUTPUT DAT uzerine yazilir.</summary>
	private OverlayResult OverlaySessionEditsOntoDat(string destPath, Action<string> progress)
	{
		OverlayResult result = new OverlayResult();
		progress = progress ?? ((Action<string>)delegate
		{
		});
		if (string.IsNullOrEmpty(destPath) || !File.Exists(destPath) || _rows.Count == 0)
		{
			return result;
		}
		Dictionary<int, List<LocRow>> dictionary = (from r in _rows
			group r by r.Did).ToDictionary((IGrouping<int, LocRow> g) => g.Key, (IGrouping<int, LocRow> g) => (from r in g
			orderby r.RecordIndex, r.GroupIndex, r.IndexInGroup
			select r).ToList());
		List<int> list = new List<int>();
		foreach (KeyValuePair<int, List<LocRow>> item in dictionary)
		{
			if ((uint)item.Key >> 24 != 37)
			{
				continue;
			}
			bool flag = false;
			foreach (LocRow item2 in item.Value)
			{
				if (IsSessionDirty(item2))
				{
					flag = true;
					break;
				}
			}
			if (flag)
			{
				list.Add(item.Key);
			}
		}
		result.DirtyDids = list.Count;
		if (list.Count == 0)
		{
			progress("Session overlay: dirty DID yok (TR kopya oldugu gibi)");
			return result;
		}
		progress($"Session overlay: {list.Count} dirty DID...");
		using DatExportSession datExportSession = new DatExportSession();
		datExportSession.Open(destPath, writable: true);
		Dictionary<int, int[]> dictionary2 = datExportSession.LoadSizeMap();
		int num = 0;
		foreach (int item3 in list)
		{
			num++;
			if (!dictionary.TryGetValue(item3, out var value))
			{
				result.Fail++;
				continue;
			}
			if (!dictionary2.TryGetValue(item3, out var value2))
			{
				result.Fail++;
				if (num % 20 == 0 || num == list.Count)
				{
					progress($"Session overlay {num}/{list.Count} OK={result.Ok} fail={result.Fail}");
				}
				continue;
			}
			int size = value2[0];
			int iteration = value2[1];
			byte[] array;
			int version;
			try
			{
				array = datExportSession.ReadSubfile(item3, size, out version);
			}
			catch
			{
				result.Fail++;
				continue;
			}
			bool flag2 = TurbineDat.LooksCompressed(array);
			byte[] array2 = (flag2 ? TurbineDat.MaybeDecompress(array) : array);
			LocBin locBin;
			try
			{
				locBin = LocBin.Parse(array2, item3);
			}
			catch
			{
				result.Fail++;
				continue;
			}
			List<LocRow> rows = locBin.GetRows(item3);
			if (rows == null || rows.Count == 0)
			{
				result.Fail++;
				continue;
			}
			Dictionary<string, LocRow> dictionary3 = new Dictionary<string, LocRow>(StringComparer.Ordinal);
			foreach (LocRow item4 in value)
			{
				dictionary3[item4.Key] = item4;
			}
			bool flag3 = false;
			for (int i = 0; i < rows.Count; i++)
			{
				string text = rows[i].Original ?? "";
				string text2 = text;
				if (dictionary3.TryGetValue(rows[i].Key, out var value3)
					&& string.Equals(value3.Original ?? "", text, StringComparison.Ordinal)
					&& !string.IsNullOrEmpty(value3.Translation))
				{
					text2 = value3.Translation;
				}
				if (text2.Length == 0)
				{
					text2 = text;
				}
				if (!string.Equals(text2, text, StringComparison.Ordinal))
				{
					bool approvedTarget = _approved.IsApprovedTarget(rows[i].Key, text, text2);
					text2 = ((!TextGuard.ShouldKeepAsIs(text) || approvedTarget) ? TextGuard.Sanitize(text, text2, allowCompact: false) : text);
				}
				rows[i].Translation = text2;
				if (!string.Equals(text2, text, StringComparison.Ordinal))
				{
					flag3 = true;
					result.DirtyRows++;
				}
			}
			if (!flag3)
			{
				result.Same++;
				continue;
			}
			byte[] array3 = null;
			if (false && locBin.UsedFlatFallback)
			{
				List<KeyValuePair<string, string>> list2 = new List<KeyValuePair<string, string>>();
				for (int j = 0; j < rows.Count; j++)
				{
					string text3 = rows[j].Original ?? "";
					string text4 = rows[j].Translation ?? text3;
					if (!string.Equals(text3, text4, StringComparison.Ordinal))
					{
						list2.Add(new KeyValuePair<string, string>(text3, text4));
					}
				}
				array3 = InPlaceLocPatch.Apply(array2, locBin, list2, out var _);
				if (array3 == null)
				{
					result.SkipFlat++;
					result.Fail++;
					continue;
				}
			}
			else
			{
				try
				{
					LocWriteGuard.Result result2 = LocWriteGuard.TryBuild(locBin, rows, array, flag2);
				if (result2 != null && result2.Blob != null)
				{
					array3 = result2.Blob;
				}
				else
				{
					Program.Log("Session guard red 0x" + item3.ToString("X8") + ": " + ((result2 == null) ? "sonuç yok" : result2.Reason));
					array3 = null;
				}
				}
				catch
				{
					array3 = null;
				}
				if (array3 == null)
				{
					result.Fail++;
					continue;
				}
			}
			try
			{
				if (datExportSession.WriteSubfile(item3, array3, version, iteration) < 0)
				{
					result.Fail++;
				}
				else
				{
					result.Ok++;
				}
			}
			catch
			{
				result.Fail++;
			}
			if (num % 20 == 0 || num == list.Count)
			{
				progress($"Session overlay {num}/{list.Count} OK={result.Ok} fail={result.Fail}");
			}
		}
		progress("Session overlay flush...");
		datExportSession.Flush();
		datExportSession.Close();
		progress($"Session overlay bitti OK={result.Ok} fail={result.Fail} dirtyDid={result.DirtyDids} dirtyRow={result.DirtyRows}");
		return result;
	}

	private static bool BytesEqual(byte[] a, byte[] b)
	{
		if (a == null || b == null || a.Length != b.Length)
		{
			return false;
		}
		for (int i = 0; i < a.Length; i++)
		{
			if (a[i] != b[i])
			{
				return false;
			}
		}
		return true;
	}
}
