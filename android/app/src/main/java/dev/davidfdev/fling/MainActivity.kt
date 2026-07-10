package dev.davidfdev.fling

import android.Manifest
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Card
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import androidx.lifecycle.viewmodel.compose.viewModel
import dev.davidfdev.fling.data.ClipItem
import dev.davidfdev.fling.data.PairedDevice
import dev.davidfdev.fling.data.Settings
import dev.davidfdev.fling.ui.MainViewModel
import dev.davidfdev.fling.ui.SettingsScreen
import dev.davidfdev.fling.ui.theme.FlingTheme
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            FlingTheme {
                FlingApp()
            }
        }
    }

    override fun onResume() {
        super.onResume()
        val app = application as FlingApplication
        CoroutineScope(Dispatchers.IO).launch {
            app.deviceRepository.refreshFlow()
        }
    }
}

@Composable
private fun FlingApp(viewModel: MainViewModel = viewModel()) {
    var showSettings by remember { mutableStateOf(false) }

    if (showSettings) {
        BackHandler { showSettings = false }
        SettingsScreen(viewModel = viewModel, onBack = { showSettings = false })
    } else {
        MainScreen(viewModel = viewModel, onOpenSettings = { showSettings = true })
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun MainScreen(viewModel: MainViewModel, onOpenSettings: () -> Unit) {
    val context = LocalContext.current
    val app = context.applicationContext as FlingApplication
    val isRunning by viewModel.serviceRunning.collectAsState()
    val isWifiConnected by viewModel.isWifiConnected.collectAsState()
    val devices by viewModel.pairedDevices.collectAsState()
    val clipItems by viewModel.clipboardItems.collectAsState()
    val settings by viewModel.settings.collectAsState()

    val permissionLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission(),
    ) { granted ->
        if (granted) {
            startFlingService(context)
        } else {
            app.setServiceRunningImmediate(false)
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Fling") },
                actions = {
                    IconButton(onClick = onOpenSettings) {
                        Icon(Icons.Filled.Settings, contentDescription = "Settings")
                    }
                },
            )
        },
    ) { innerPadding ->
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .padding(innerPadding)
                .padding(horizontal = 16.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            item {
                ServiceStatusCard(
                    isRunning = isRunning,
                    isWifiConnected = isWifiConnected,
                    port = settings.port,
                    deviceName = settings.deviceName,
                    onToggle = { enabled ->
                        app.setServiceRunningImmediate(enabled)
                        if (enabled) {
                            if (needsNotificationPermission(context)) {
                                permissionLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
                            } else {
                                startFlingService(context)
                            }
                        } else {
                            stopFlingService(context)
                        }
                    },
                )
            }

            item {
                SectionHeader("Paired Devices")
            }

            if (devices.isEmpty()) {
                item {
                    Text(
                        text = "No paired devices",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(vertical = 8.dp),
                    )
                }
            } else {
                items(devices, key = { it.apiKey }) { device ->
                    PairedDeviceRow(
                        device = device,
                        onUnpair = { viewModel.unpairDevice(device.apiKey) },
                    )
                }
            }

            item {
                Spacer(modifier = Modifier.height(8.dp))
                SectionHeader(
                    title = "Recent Clips",
                    action = if (clipItems.isNotEmpty()) {
                        { TextButton(onClick = { viewModel.clearClips() }) { Text("Clear") } }
                    } else {
                        null
                    },
                )
            }

            if (clipItems.isEmpty()) {
                item {
                    Text(
                        text = "No clips received yet",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(vertical = 8.dp),
                    )
                }
            } else {
                items(clipItems, key = { it.receivedAt }) { clipItem ->
                    ClipItemRow(
                        clipItem = clipItem,
                        onRemove = { viewModel.removeClip(clipItem) },
                    )
                }
            }

            item { Spacer(modifier = Modifier.height(16.dp)) }
        }
    }
}

@Composable
private fun ServiceStatusCard(
    isRunning: Boolean,
    isWifiConnected: Boolean,
    port: Int,
    deviceName: String,
    onToggle: (Boolean) -> Unit,
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(modifier = Modifier.padding(16.dp)) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = if (isRunning) "Fling is running" else "Fling is stopped",
                        style = MaterialTheme.typography.titleMedium,
                    )
                    if (isRunning) {
                        if (deviceName.isNotBlank()) {
                            Text(
                                text = deviceName,
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                            )
                        }
                        val ip = remember { MainViewModel.getDeviceIp() }
                        Text(
                            text = if (ip != null) "$ip:$port" else "Not connected",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }
                Switch(checked = isRunning, onCheckedChange = onToggle)
            }
            if (isRunning && !isWifiConnected) {
                Spacer(modifier = Modifier.height(8.dp))
                Text(
                    text = "Not connected to Wi-Fi — devices on the local network cannot reach Fling.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.error,
                )
            }
        }
    }
}

@Composable
private fun SectionHeader(title: String, action: @Composable (() -> Unit)? = null) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(
            text = title,
            style = MaterialTheme.typography.titleSmall,
            color = MaterialTheme.colorScheme.primary,
        )
        if (action != null) {
            action()
        }
    }
    HorizontalDivider()
}

@Composable
private fun PairedDeviceRow(device: PairedDevice, onUnpair: () -> Unit) {
    var showConfirmDialog by remember { mutableStateOf(false) }

    if (showConfirmDialog) {
        AlertDialog(
            onDismissRequest = { showConfirmDialog = false },
            title = { Text("Unpair device") },
            text = { Text("Remove \"${device.name}\" from paired devices?") },
            confirmButton = {
                TextButton(onClick = {
                    showConfirmDialog = false
                    onUnpair()
                }) {
                    Text("Remove")
                }
            },
            dismissButton = {
                TextButton(onClick = { showConfirmDialog = false }) {
                    Text("Cancel")
                }
            },
        )
    }

    Card(
        modifier = Modifier.fillMaxWidth(),
        onClick = { showConfirmDialog = true },
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(12.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(text = device.name, style = MaterialTheme.typography.bodyLarge)
            Text(
                text = formatDate(device.pairedAt),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

@Composable
private fun ClipItemRow(clipItem: ClipItem, onRemove: () -> Unit) {
    val context = LocalContext.current
    var showDialog by remember { mutableStateOf(false) }

    if (showDialog) {
        AlertDialog(
            onDismissRequest = { showDialog = false },
            title = {
                Text(
                    when {
                        clipItem.type == "image/png" -> "Image"
                        clipItem.type.startsWith("text/") -> {
                            val text = String(clipItem.data)
                            if (text.length > 40) text.take(40) + "…" else text
                        }
                        else -> clipItem.type
                    },
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            },
            text = { Text(formatDate(clipItem.receivedAt)) },
            confirmButton = {
                TextButton(onClick = {
                    showDialog = false
                    copyClipToClipboard(context, clipItem)
                }) {
                    Text("Copy")
                }
            },
            dismissButton = {
                Row {
                    TextButton(onClick = {
                        showDialog = false
                        shareClip(context, clipItem)
                    }) {
                        Text("Share")
                    }
                    TextButton(onClick = {
                        showDialog = false
                        onRemove()
                    }) {
                        Text("Clear")
                    }
                }
            },
        )
    }

    Card(
        onClick = { showDialog = true },
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(modifier = Modifier.padding(12.dp)) {
            Text(
                text = when {
                    clipItem.type == "image/png" -> "[Image]"
                    clipItem.type.startsWith("text/") -> {
                        val text = String(clipItem.data)
                        if (text.length > 100) text.take(100) + "…" else text
                    }
                    else -> "[${clipItem.type}]"
                },
                style = MaterialTheme.typography.bodyMedium,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = formatDate(clipItem.receivedAt),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 4.dp),
            )
        }
    }
}

private fun copyClipToClipboard(context: android.content.Context, clipItem: ClipItem) {
    val clipboardManager = context.getSystemService(ClipboardManager::class.java)
    when {
        clipItem.type.startsWith("text/") -> {
            val text = String(clipItem.data)
            clipboardManager.setPrimaryClip(ClipData.newPlainText("Fling", text))
        }
        clipItem.type == "image/png" -> {
            val uri = writeImageToCache(context, clipItem)
            clipboardManager.setPrimaryClip(ClipData.newUri(context.contentResolver, "Fling", uri))
        }
    }
    Toast.makeText(context, "Copied to clipboard", Toast.LENGTH_SHORT).show()
}

private fun shareClip(context: android.content.Context, clipItem: ClipItem) {
    val intent = Intent(Intent.ACTION_SEND).apply {
        when {
            clipItem.type.startsWith("text/") -> {
                type = "text/plain"
                putExtra(Intent.EXTRA_TEXT, String(clipItem.data))
            }
            clipItem.type == "image/png" -> {
                type = "image/png"
                val uri = writeImageToCache(context, clipItem)
                putExtra(Intent.EXTRA_STREAM, uri)
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            }
        }
    }
    context.startActivity(Intent.createChooser(intent, null))
}

private fun writeImageToCache(context: android.content.Context, clipItem: ClipItem): android.net.Uri {
    val dir = java.io.File(context.cacheDir, "clip_images").apply { mkdirs() }
    val file = java.io.File(dir, "clip_${clipItem.receivedAt}.png")
    file.writeBytes(clipItem.data)
    return androidx.core.content.FileProvider.getUriForFile(
        context,
        "${context.packageName}.fileprovider",
        file,
    )
}

private fun formatDate(timestamp: Long): String {
    val format = SimpleDateFormat("MMM d, h:mm a", Locale.getDefault())
    return format.format(Date(timestamp))
}

private fun needsNotificationPermission(context: android.content.Context): Boolean =
    Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
        ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) !=
        PackageManager.PERMISSION_GRANTED

private fun startFlingService(context: android.content.Context) {
    val intent = Intent(context, FlingService::class.java)
    ContextCompat.startForegroundService(context, intent)
}

private fun stopFlingService(context: android.content.Context) {
    context.stopService(Intent(context, FlingService::class.java))
}
