// Enabling both UI frameworks makes several type names ambiguous. WPF is the app model
// here — WinForms is present only for NotifyIcon — so the WPF types win by default and
// the WinForms ones are spelled out where they are genuinely wanted.
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using KeyEventArgs = System.Windows.Input.KeyEventArgs;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;
global using DragEventArgs = System.Windows.DragEventArgs;
global using DataFormats = System.Windows.DataFormats;
global using DragDropEffects = System.Windows.DragDropEffects;
global using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

// Clipboard is deliberately left ambiguous. Both frameworks define it, and neither
// should be used here — clipboard access goes through Core's IClipboardReader.
