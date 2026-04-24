using System;
using System.Diagnostics;
using Godot;

public static class GameState
{
    public static string CharacterName { get; set; } = "Hero";
    public static int AccountId { get; set; } = 0;
    public static string AccountName { get; set; } = "";
    private static Process _serverProcess;

    // Attach to exit event to kill node headless process gracefully
    static GameState()
    {
        AppDomain.CurrentDomain.ProcessExit += (sender, e) => StopServer();
    }

    public static void StartServer()
    {
        if (_serverProcess != null && !_serverProcess.HasExited) return;

        // Kill any stale node server from a previous run
        try {
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName("node"))
            {
                try { proc.Kill(); proc.WaitForExit(2000); } catch {}
            }
        } catch {}

        // Give the OS a moment to release the port
        System.Threading.Thread.Sleep(500);

        try 
        {
            _serverProcess = new Process();
            _serverProcess.StartInfo.FileName = "node";
            _serverProcess.StartInfo.Arguments = "index.js";
            _serverProcess.StartInfo.WorkingDirectory = ProjectSettings.GlobalizePath("res://../server");
            _serverProcess.StartInfo.UseShellExecute = false;
            _serverProcess.StartInfo.CreateNoWindow = true;
            _serverProcess.StartInfo.RedirectStandardOutput = true;
            _serverProcess.StartInfo.RedirectStandardError = true;
            _serverProcess.OutputDataReceived += (s, e) => { if (e.Data != null) GD.Print($"[SERVER] {e.Data}"); };
            _serverProcess.ErrorDataReceived += (s, e) => { if (e.Data != null) GD.PrintErr($"[SERVER] {e.Data}"); };
            _serverProcess.Start();
            _serverProcess.BeginOutputReadLine();
            _serverProcess.BeginErrorReadLine();
            GD.Print("[SYSTEM] Local Node server spawned successfully.");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[SYSTEM] Failed to spawn local server: {e.Message}");
        }
    }

    public static void StopServer()
    {
        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            try {
                _serverProcess.Kill();
                _serverProcess.Dispose();
                _serverProcess = null;
                GD.Print("[SYSTEM] Killed local Node server.");
            } catch {}
        }
    }
}
