namespace ComparePhotoInExploer;

/// <summary>
/// Form1 的键盘处理逻辑
/// </summary>
public partial class Form1
{
    private void Form1_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.H)
        {
            _showHelp = !_showHelp;
            if (_showHelp)
                _historyBarData.Collapse();
            this.Invalidate();
        }
        else if (e.KeyCode == Keys.Escape)
        {
            // Esc优先关闭当前打开的界面，只有都关闭时才关闭程序
            if (_resetOverlay.IsVisible)
            {
                _resetOverlay.Hide();
                this.Invalidate();
            }
            else if (_showHelp || _showZoomHelp)
            {
                _showHelp = false;
                _showZoomHelp = false;
                this.Invalidate();
            }
            else if (!_historyBarData.IsCollapsed)
            {
                _historyBarData.Collapse();
                _hoverHistoryGroup = -1;
                this.Invalidate();
            }
            else
            {
                this.Close();
            }
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Alt || keyData == (Keys.Alt | Keys.Menu))
            return true;
        if (keyData == (Keys.Control | Keys.W))
        {
            // Ctrl+W优先关闭当前打开的界面，只有都关闭时才关闭程序
            if (_resetOverlay.IsVisible)
            {
                _resetOverlay.Hide();
                this.Invalidate();
            }
            else if (_showHelp || _showZoomHelp)
            {
                _showHelp = false;
                _showZoomHelp = false;
                this.Invalidate();
            }
            else if (!_historyBarData.IsCollapsed)
            {
                _historyBarData.Collapse();
                _hoverHistoryGroup = -1;
                this.Invalidate();
            }
            else
            {
                this.Close();
            }
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private bool IsAltPressed()
    {
        return (ModifierKeys & Keys.Alt) == Keys.Alt;
    }
}
