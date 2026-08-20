// Enabling both UI frameworks makes several type names ambiguous. WPF is the app model
// here — WinForms is present only for NotifyIcon — so the WPF types win by default and
// the WinForms ones are spelled out where they are genuinely wanted.
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
