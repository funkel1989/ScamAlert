namespace ScamAlert.Configurator;

internal sealed class MainForm : Form
{
    private readonly PairingSetupService _pairing;
    private readonly TextBox? _apiUrlBox;
    private readonly TextBox _codeBox;
    private readonly Label _statusLabel;
    private readonly Button _connectButton;

    private readonly string? _bakedApiUrl;

    public MainForm(PairingSetupService pairing)
    {
        _pairing = pairing;
        _bakedApiUrl = PairingSetupService.ReadDefaultApiBaseUrlFromRegistry();
        var apiUrlLocked = !string.IsNullOrWhiteSpace(_bakedApiUrl);
        Text = "ScamAlert — pair this PC";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(480, 280);

        var intro = new Label
        {
            AutoSize = false,
            Location = new Point(16, 12),
            Size = new Size(448, apiUrlLocked ? 56 : 48),
            Text = apiUrlLocked
                ? "Open your ScamAlert account in the browser, go to Devices, click Pair PC, and enter the code below."
                : "Sign in at the ScamAlert website, open Devices, and click Pair PC. Enter your website and code below."
        };

        Label? apiLabel = null;
        TextBox? apiUrlBox = null;
        if (!apiUrlLocked)
        {
            apiLabel = new Label { AutoSize = true, Location = new Point(16, 68), Text = "Website address" };
            apiUrlBox = new TextBox
            {
                Location = new Point(16, 88),
                Size = new Size(448, 23),
                PlaceholderText = "https://app.scamalert.com"
            };
            _apiUrlBox = apiUrlBox;
        }

        var codeTop = apiUrlLocked ? 76 : 120;
        var codeLabel = new Label { AutoSize = true, Location = new Point(16, codeTop), Text = "Pairing code" };
        _codeBox = new TextBox
        {
            CharacterCasing = CharacterCasing.Upper,
            Font = new Font(Font.FontFamily, 14, FontStyle.Bold),
            Location = new Point(16, codeTop + 20),
            MaxLength = 12,
            Size = new Size(200, 30)
        };

        var buttonTop = codeTop + 62;
        _connectButton = new Button
        {
            Location = new Point(16, buttonTop),
            Size = new Size(120, 32),
            Text = "Connect"
        };
        _connectButton.Click += async (_, _) => await ConnectAsync();

        var skip = new LinkLabel
        {
            AutoSize = true,
            Location = new Point(150, buttonTop + 6),
            Text = "Skip for now"
        };
        skip.LinkClicked += (_, _) => Close();

        _statusLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, buttonTop + 40),
            Size = new Size(448, 48),
            ForeColor = SystemColors.GrayText
        };

        if (apiUrlLocked)
        {
            Controls.AddRange([intro, codeLabel, _codeBox, _connectButton, skip, _statusLabel]);
        }
        else
        {
            Controls.AddRange([intro, apiLabel!, apiUrlBox!, codeLabel, _codeBox, _connectButton, skip, _statusLabel]);
        }
        AcceptButton = _connectButton;
    }

    private async Task ConnectAsync()
    {
        _connectButton.Enabled = false;
        _statusLabel.ForeColor = SystemColors.GrayText;
        _statusLabel.Text = "Connecting…";

        try
        {
            var apiUrl = _apiUrlBox?.Text ?? _bakedApiUrl ?? string.Empty;
            var result = await _pairing.RedeemAsync(apiUrl, _codeBox.Text, CancellationToken.None);
            BrokerCloudConfigWriter.Write(result);
            PairingSetupService.TryRestartBrokerService(out var restartNote);

            _statusLabel.ForeColor = Color.DarkGreen;
            _statusLabel.Text = restartNote is null
                ? $"Connected \"{result.DeviceName}\". This PC is now linked to your account."
                : $"{restartNote}\nLinked device: {result.DeviceName}.";

            MessageBox.Show(
                this,
                $"This PC is paired as {result.DeviceName}.\n\nKeep ScamAlert Tray running in the system tray.",
                "ScamAlert",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _statusLabel.ForeColor = Color.DarkRed;
            _statusLabel.Text = ex.Message;
        }
        finally
        {
            _connectButton.Enabled = true;
        }
    }
}
