package dev.davidfdev.fling.ui

import android.content.Context
import android.content.Intent
import android.os.PowerManager
import android.provider.Settings
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.Button
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
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
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SettingsScreen(
    viewModel: MainViewModel,
    onBack: () -> Unit,
) {
    val settings by viewModel.settings.collectAsState()

    var portText by remember(settings.port) { mutableStateOf(settings.port.toString()) }
    var portError by remember { mutableStateOf<String?>(null) }
    var nameText by remember(settings.deviceName) { mutableStateOf(settings.deviceName) }
    var nameError by remember { mutableStateOf<String?>(null) }
    var showPortMessage by remember { mutableStateOf(false) }

    val context = LocalContext.current
    var batteryOptimized by remember {
        mutableStateOf(isBatteryOptimizationDisabled(context))
    }

    val lifecycleOwner = LocalLifecycleOwner.current
    androidx.compose.runtime.DisposableEffect(lifecycleOwner) {
        val observer = LifecycleEventObserver { _, event ->
            if (event == Lifecycle.Event.ON_RESUME) {
                batteryOptimized = isBatteryOptimizationDisabled(context)
            }
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose { lifecycleOwner.lifecycle.removeObserver(observer) }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Settings") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back")
                    }
                },
            )
        },
    ) { innerPadding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(innerPadding)
                .padding(horizontal = 16.dp)
                .verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Text(
                text = "Device Name",
                style = MaterialTheme.typography.titleSmall,
                color = MaterialTheme.colorScheme.primary,
            )

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
                verticalAlignment = Alignment.Top,
            ) {
                OutlinedTextField(
                    value = nameText,
                    onValueChange = { value ->
                        nameText = value
                        nameError = null
                    },
                    label = { Text("Name") },
                    isError = nameError != null,
                    supportingText = nameError?.let { msg -> { Text(msg) } },
                    singleLine = true,
                    modifier = Modifier.weight(1f),
                )
                IconButton(
                    onClick = { viewModel.regenerateDeviceName() },
                    modifier = Modifier.padding(top = 8.dp),
                ) {
                    Icon(Icons.Filled.Refresh, contentDescription = "Generate random name")
                }
            }

            if (nameText != settings.deviceName && nameError == null) {
                if (nameText.isBlank()) {
                    nameError = "Name must not be blank"
                } else {
                    viewModel.updateDeviceName(nameText)
                }
            }

            Spacer(modifier = Modifier.height(8.dp))

            Text(
                text = "Server",
                style = MaterialTheme.typography.titleSmall,
                color = MaterialTheme.colorScheme.primary,
            )

            OutlinedTextField(
                value = portText,
                onValueChange = { value ->
                    portText = value
                    portError = null
                    showPortMessage = false
                    val port = value.toIntOrNull()
                    if (port == null) {
                        portError = "Must be a number"
                    } else if (port !in 1..65535) {
                        portError = "Must be between 1 and 65535"
                    } else if (port != settings.port) {
                        viewModel.updatePort(port)
                        showPortMessage = true
                    }
                },
                label = { Text("Port") },
                isError = portError != null,
                supportingText = when {
                    portError != null -> { -> Text(portError!!) }
                    showPortMessage -> { -> Text(
                        "Restart the service for changes to take effect",
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    ) }
                    else -> null
                },
                singleLine = true,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                modifier = Modifier.fillMaxWidth(),
            )

            Spacer(modifier = Modifier.height(8.dp))

            Text(
                text = "Battery",
                style = MaterialTheme.typography.titleSmall,
                color = MaterialTheme.colorScheme.primary,
            )

            if (batteryOptimized) {
                Text(
                    text = "Battery optimization is disabled for Fling.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            } else {
                Text(
                    text = "Fling may be stopped by the system to save battery. Disable battery optimization to keep it running reliably.",
                    style = MaterialTheme.typography.bodyMedium,
                )
                Button(onClick = { openBatterySettings(context) }) {
                    Text("Open battery settings")
                }
            }

            Spacer(modifier = Modifier.height(16.dp))
        }
    }
}

private fun isBatteryOptimizationDisabled(context: Context): Boolean {
    val pm = context.getSystemService(Context.POWER_SERVICE) as PowerManager
    return pm.isIgnoringBatteryOptimizations(context.packageName)
}

private fun openBatterySettings(context: Context) {
    runCatching {
        context.startActivity(
            Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
        )
    }
}
