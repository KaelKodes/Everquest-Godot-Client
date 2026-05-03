using Godot;
using System.Text.Json;

public partial class GroupMemberRow : Button
{
	private ProgressBar _hpBar;
	private ProgressBar _manaBar;
	private ProgressBar _enduranceBar;
	private ProgressBar _hateBar;
	private Label _nameLabel;

	private string _memberName;

	public override void _Ready()
	{
		_hpBar = GetNode<ProgressBar>("%HPBar");
		_manaBar = GetNode<ProgressBar>("%ManaBar");
		_enduranceBar = GetNode<ProgressBar>("%EnduranceBar");
		_hateBar = GetNode<ProgressBar>("%HateBar");
		_nameLabel = GetNode<Label>("%NameLabel");

		Pressed += OnPressed;
	}

	public void UpdateMember(JsonElement data)
	{
		_memberName = data.TryGetProperty("name", out var n) ? n.GetString() : "Unknown";
		_nameLabel.Text = _memberName;

		// Stats
		int hp = data.TryGetProperty("hp", out var h) ? h.GetInt32() : 0;
		int maxHp = data.TryGetProperty("maxHp", out var mh) ? mh.GetInt32() : 100;
		_hpBar.MaxValue = maxHp;
		_hpBar.Value = hp;

		int mp = data.TryGetProperty("mana", out var m) ? m.GetInt32() : 0;
		int maxMp = data.TryGetProperty("maxMana", out var mm) ? mm.GetInt32() : 0;
		
		if (maxMp > 0)
		{
			_manaBar.Visible = true;
			_manaBar.MaxValue = maxMp;
			_manaBar.Value = mp;
		}
		else
		{
			_manaBar.Visible = false;
		}

		int end = data.TryGetProperty("endurance", out var e) ? e.GetInt32() : 0;
		int maxEnd = data.TryGetProperty("maxEndurance", out var me) ? me.GetInt32() : 100;
		_enduranceBar.MaxValue = maxEnd;
		_enduranceBar.Value = end;

		// Hate / Aggro (0-100%)
		// In EQ this often shows relative hate. We'll use it as a simple slider for now.
		float hatePct = data.TryGetProperty("hatePct", out var hpct) ? (float)hpct.GetDouble() : 0f;
		_hateBar.Position = new Vector2(hatePct * (Size.X - 20) / 100f, _hateBar.Position.Y);
		
		// Dim if out of zone
		string zoneId = data.TryGetProperty("zoneId", out var z) ? z.GetString() : "";
		Modulate = string.IsNullOrEmpty(zoneId) ? new Color(0.5f, 0.5f, 0.5f, 1.0f) : new Color(1, 1, 1, 1);
	}

	private void OnPressed()
	{
		if (string.IsNullOrEmpty(_memberName)) return;
		
		// Trigger targeting via MainUI
		var mainUI = GetTree().Root.FindChild("MainUI", true, false) as MainUI;
		if (mainUI != null)
		{
			mainUI.ExecuteCommand($"/target {_memberName}");
		}
	}
}
