using System.Windows;
using System.Windows.Controls;

namespace VSAgent.Views
{
    /// <summary>
    /// Placeholder UserControl for the Skills tab. Filled in by
    /// VSAgentControl after the package initializes.
    /// </summary>
    public sealed class SkillsPlaceholder : UserControl
    {
        public SkillsPlaceholder()
        {
            var tb = new TextBlock
            {
                Text = "Loading skills...",
                Margin = new Thickness(16),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            Content = tb;
        }
    }

    /// <summary>
    /// Placeholder for the Tools tab (custom MCP tools).
    /// </summary>
    public sealed class ToolsPlaceholder : UserControl
    {
        public ToolsPlaceholder()
        {
            var tb = new TextBlock
            {
                Text = "Loading tools...",
                Margin = new Thickness(16),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            Content = tb;
        }
    }
}
