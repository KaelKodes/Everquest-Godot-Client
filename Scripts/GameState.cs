using System;
using System.Diagnostics;
using Godot;

public static class GameState
{
    public static string CharacterName { get; set; } = "Hero";
    public static int AccountId { get; set; } = 0;
    public static string AccountName { get; set; } = "";

    // Attach to exit event to kill node headless process gracefully
    static GameState()
    {
    }
}
