using Godot;
using System.Collections.Generic;
using System.Text.Json;

public partial class GroupWindow : Window
{
	private VBoxContainer _memberContainer;
	private Button _inviteBtn;
	private Button _disbandBtn;
	private ConfirmationDialog _kickDialog;

	private PackedScene _memberRowScene = GD.Load<PackedScene>("res://Scenes/UI/GroupMemberRow.tscn");
	private List<GroupMemberRow> _activeRows = new();
	private string _currentTargetName = "";
	private List<string> _currentMemberNames = new();

	public override void _Ready()
	{
		_memberContainer = GetNode<VBoxContainer>("%MemberContainer");
		_inviteBtn = GetNode<Button>("%InviteBtn");
		_disbandBtn = GetNode<Button>("%DisbandBtn");

		_inviteBtn.Pressed += OnInvitePressed;
		_disbandBtn.Pressed += OnDisbandPressed;

		CloseRequested += () => Hide();

		// Setup Kick Confirmation
		_kickDialog = new ConfirmationDialog();
		AddChild(_kickDialog);
		_kickDialog.Confirmed += OnKickConfirmed;
	}

	public void UpdateGroup(JsonElement members, JsonElement roles)
	{
		// Clear existing rows
		foreach (var row in _activeRows) row.QueueFree();
		_activeRows.Clear();
		_currentMemberNames.Clear();

		if (members.ValueKind != JsonValueKind.Array) return;

		foreach (var member in members.EnumerateArray())
		{
			var row = _memberRowScene.Instantiate<GroupMemberRow>();
			_memberContainer.AddChild(row);
			row.UpdateMember(member);
			_activeRows.Add(row);
			
			string mName = member.GetProperty("name").GetString();
			_currentMemberNames.Add(mName);
		}

		UpdateInviteButton();
	}

	public void OnTargetChanged(string name, string type)
	{
		_currentTargetName = name;
		UpdateInviteButton();
	}

	private void UpdateInviteButton()
	{
		if (string.IsNullOrEmpty(_currentTargetName) || _currentTargetName == "No Target")
		{
			_inviteBtn.Text = "Invite";
			_inviteBtn.Disabled = true;
			return;
		}

		_inviteBtn.Disabled = false;
		if (_currentMemberNames.Contains(_currentTargetName))
		{
			_inviteBtn.Text = "Dismiss";
		}
		else
		{
			_inviteBtn.Text = "Invite";
		}
	}

	private void OnInvitePressed()
	{
		if (string.IsNullOrEmpty(_currentTargetName)) return;

		if (_inviteBtn.Text == "Dismiss")
		{
			_kickDialog.DialogText = $"Are you sure you want to kick {_currentTargetName}?";
			_kickDialog.PopupCentered();
		}
		else
		{
			var mainUI = GetTree().Root.FindChild("MainUI", true, false) as MainUI;
			if (mainUI != null) mainUI.ExecuteCommand($"/invite {_currentTargetName}");
		}
	}

	private void OnKickConfirmed()
	{
		var mainUI = GetTree().Root.FindChild("MainUI", true, false) as MainUI;
		if (mainUI != null)
		{
			// Sending custom packet or command for kick
			// We'll use the network message we wired: GROUP_KICK
			mainUI.SendNetworkMessage("GROUP_KICK", new Dictionary<string, object> { { "targetName", _currentTargetName } });
		}
	}

	private void OnDisbandPressed()
	{
		var mainUI = GetTree().Root.FindChild("MainUI", true, false) as MainUI;
		if (mainUI != null) mainUI.ExecuteCommand("/disband");
	}
}
