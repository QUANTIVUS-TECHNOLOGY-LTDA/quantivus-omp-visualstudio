using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;
using VSAgent.Views;

namespace VSAgent.ToolWindows
{
    [Guid("13085c70-5c8f-4d02-8c7e-0b3d3b6e1a2f")]
    public class VSAgentToolWindow : ToolWindowPane
    {
        public VSAgentToolWindow() : base(null)
        {
            Caption = "Quantivus OMP";
            Content = new VSAgentControl();
        }
    }
}
