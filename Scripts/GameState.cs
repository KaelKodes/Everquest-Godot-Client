using System;
using System.Diagnostics;
using Godot;

public static class GameState
{
    public static string CharacterName { get; set; } = "Hero";
    public static int AccountId { get; set; } = 0;
    public static string AccountName { get; set; } = "";
    public static string AccountPassword { get; set; } = "";
    public static string ServerName { get; set; } = "eqmud";

    // Hire Student Transition State
    public static bool IsCreatingStudent { get; set; } = false;
    public static string PendingStudentName { get; set; } = "";
    public static int PendingStudentRaceId { get; set; } = 1;
    public static int PendingStudentClassId { get; set; } = 1;
    public static int PendingStudentLevel { get; set; } = 1;

    // Attach to exit event to kill node headless process gracefully
    static GameState()
    {
    }
}
