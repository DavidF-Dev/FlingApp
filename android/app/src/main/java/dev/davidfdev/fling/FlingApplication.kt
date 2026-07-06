package dev.davidfdev.fling

import android.app.Application
import android.content.Intent
import androidx.core.content.ContextCompat
import dev.davidfdev.fling.data.ClipboardBuffer
import dev.davidfdev.fling.data.DeviceRepository
import dev.davidfdev.fling.data.SettingsRepository
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

class FlingApplication : Application() {

    private val appScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    lateinit var deviceRepository: DeviceRepository
        private set
    lateinit var clipboardBuffer: ClipboardBuffer
        private set
    lateinit var settingsRepository: SettingsRepository
        private set

    private val _serviceRunning = MutableStateFlow(false)
    val serviceRunning: StateFlow<Boolean> = _serviceRunning.asStateFlow()

    override fun onCreate() {
        super.onCreate()
        deviceRepository = DeviceRepository(filesDir.resolve("paired_devices.json"))
        clipboardBuffer = ClipboardBuffer()
        settingsRepository = SettingsRepository(this)

        appScope.launch {
            settingsRepository.initializeDefaults()
            val settings = settingsRepository.get()
            if (settings.serviceEnabled) {
                val intent = Intent(this@FlingApplication, FlingService::class.java)
                ContextCompat.startForegroundService(this@FlingApplication, intent)
            }
        }
    }

    fun setServiceRunning(running: Boolean) {
        _serviceRunning.value = running
        appScope.launch {
            settingsRepository.updateServiceEnabled(running)
        }
    }
}
