using System.Windows.Controls;

namespace LoLHelper.PCClient.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }
    
    public void UpdateConnection(string status, System.Windows.Media.Brush color)
    {
        ConnectionStatusText.Text = status;
        ConnectionStatusText.Foreground = color;
    }
    
    public void UpdateSummoner(string name)
    {
        SummonerNameText.Text = name;
    }
    
    public void UpdateLogs(string log)
    {
        RawJsonText.Text = log;
    }
}
