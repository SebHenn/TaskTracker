using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;

namespace TaskTracker.Controls
{
    public partial class CustomTextBox : Control
    {
        private TextBox _textBox;

        static CustomTextBox()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(CustomTextBox),
                new FrameworkPropertyMetadata(typeof(CustomTextBox)));
        }

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(CustomTextBox), new PropertyMetadata(string.Empty));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public static readonly DependencyProperty HintProperty =
            DependencyProperty.Register("Hint", typeof(string), typeof(CustomTextBox), new PropertyMetadata(string.Empty));

        public string Hint
        {
            get => (string)GetValue(HintProperty);
            set => SetValue(HintProperty, value);
        }

        [RelayCommand]
        public void OnClear()
        {
            Text = string.Empty;
        }

        /// <summary>Puts keyboard focus into the inner text box (e.g. for Ctrl+F).</summary>
        public void FocusText()
        {
            _textBox?.Focus();
        }

        public CustomTextBox()
        {

        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _textBox = GetTemplateChild("PART_TextBox") as TextBox;
            if (_textBox != null)
            {
                _textBox.TextChanged += (s, e) => SetCurrentValue(TextProperty, _textBox.Text);
            }
        }
    }
}
