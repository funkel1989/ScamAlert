using ScamAlert.Contracts;

namespace ScamAlert.Tray;

public sealed class ConnectionPromptForm : Form
{
    private readonly CheckBox rememberCheckBox = new()
    {
        Text = "Remember this IP for protected remote access",
        AutoSize = true
    };

    private DecisionPromptResponse? response;

    public ConnectionPromptForm(DecisionPromptRequest request)
    {
        Text = "ScamAlert";
        ClientSize = new Size(460, 220);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var title = new Label
        {
            Text = "Protected remote access attempt",
            AutoSize = false,
            Font = new Font(Font, FontStyle.Bold),
            Location = new Point(20, 18),
            Size = new Size(420, 24)
        };

        var details = new TableLayoutPanel
        {
            AutoSize = false,
            ColumnCount = 2,
            Location = new Point(20, 52),
            Size = new Size(420, 72)
        };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddDetail(details, 0, "Source IP", request.SourceIp);
        AddDetail(details, 1, "Destination port", request.DestinationPort.ToString());
        AddDetail(details, 2, "Protected service", request.ProtectedService.ToString());

        rememberCheckBox.Location = new Point(20, 134);

        var allow = new Button
        {
            Text = "Allow Once",
            DialogResult = DialogResult.OK,
            Location = new Point(184, 172),
            Size = new Size(120, 32)
        };

        var block = new Button
        {
            Text = "Block Once",
            DialogResult = DialogResult.OK,
            Location = new Point(320, 172),
            Size = new Size(120, 32)
        };

        allow.Click += (_, _) => Complete(request, UserDecisionKind.AllowOnce);
        block.Click += (_, _) => Complete(request, UserDecisionKind.BlockOnce);

        Controls.Add(title);
        Controls.Add(details);
        Controls.Add(rememberCheckBox);
        Controls.Add(allow);
        Controls.Add(block);

        AcceptButton = allow;
        CancelButton = block;
    }

    public DecisionPromptResponse? PromptResponse => response;

    private static void AddDetail(TableLayoutPanel details, int row, string label, string value)
    {
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        details.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        details.Controls.Add(new Label { Text = value, AutoSize = true, Anchor = AnchorStyles.Left }, 1, row);
    }

    private void Complete(DecisionPromptRequest request, UserDecisionKind decision)
    {
        response = new DecisionPromptResponse(
            request.ObservedEventId,
            decision,
            rememberCheckBox.Checked);

        DialogResult = DialogResult.OK;
        Close();
    }
}
