package dev.davidfdev.fling

import android.app.Application
import dev.davidfdev.fling.data.ClipboardBuffer
import dev.davidfdev.fling.data.DeviceRepository
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

class FlingApplication : Application() {

    lateinit var deviceRepository: DeviceRepository
        private set
    lateinit var clipboardBuffer: ClipboardBuffer
        private set

    private val _serviceRunning = MutableStateFlow(false)
    val serviceRunning: StateFlow<Boolean> = _serviceRunning.asStateFlow()

    override fun onCreate() {
        super.onCreate()
        deviceRepository = DeviceRepository(filesDir.resolve("paired_devices.json"))
        clipboardBuffer = ClipboardBuffer()
    }

    fun setServiceRunning(running: Boolean) {
        _serviceRunning.value = running
    }
}
