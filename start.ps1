$ErrorActionPreference = 'Stop'
$source = @((Join-Path $PSScriptRoot 'CodexAccountReader.cs'), (Join-Path $PSScriptRoot 'WidgetV2.cs'), (Join-Path $PSScriptRoot 'WidgetCompact.cs'))
$context = Join-Path $PSScriptRoot 'settings-context'
Add-Type -Path $source -ReferencedAssemblies 'System.Windows.Forms', 'System.Drawing', 'System.Web.Extensions'
[CodexUsageWidgetV2]::Main(@($context))
