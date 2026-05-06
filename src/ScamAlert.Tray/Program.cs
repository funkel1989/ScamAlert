namespace ScamAlert.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var notifyIcon = new NotifyIcon
        {
            Text = "ScamAlert",
            Visible = true,
            Icon = SystemIcons.Shield,
            ContextMenuStrip = new ContextMenuStrip()
        };

        notifyIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Application.Exit());

        var uiContext = SynchronizationContext.Current;
        if (uiContext is null)
        {
            uiContext = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(uiContext);
        }

        using var promptServer = new PromptPipeServer(uiContext);
        promptServer.Start();

        Application.Run();
    }
}
