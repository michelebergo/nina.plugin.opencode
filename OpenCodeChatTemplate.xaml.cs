using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Controls;

namespace NINA.Plugin.OpenCode {

    [Export(typeof(ResourceDictionary))]
    public partial class OpenCodeChatTemplate : ResourceDictionary {

        /// <summary>
        /// Follow new output, unless the user has scrolled up to read something. Yanking
        /// the view back to the bottom while somebody is reading what the agent did is the
        /// fastest way to make a panel unusable.
        /// </summary>
        private bool followTail = true;

        public OpenCodeChatTemplate() {
            InitializeComponent();
        }

        private void TranscriptScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e) {
            if (sender is not ScrollViewer scrollViewer) { return; }

            if (e.ExtentHeightChange == 0) {
                followTail = scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight;
            } else if (followTail) {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.ExtentHeight);
            }
        }
    }
}
