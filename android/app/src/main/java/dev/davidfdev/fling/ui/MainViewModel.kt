package dev.davidfdev.fling.ui

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import dev.davidfdev.fling.FlingApplication
import dev.davidfdev.fling.data.ClipImageCache
import dev.davidfdev.fling.data.ClipItem
import dev.davidfdev.fling.data.DeviceNameGenerator
import dev.davidfdev.fling.data.PairedDevice
import dev.davidfdev.fling.data.Settings
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import java.net.Inet4Address
import java.net.NetworkInterface

class MainViewModel(application: Application) : AndroidViewModel(application) {

    private val app = application as FlingApplication

    val serviceRunning: StateFlow<Boolean> = app.serviceRunning

    val isWifiConnected: StateFlow<Boolean> = app.connectivityObserver.isWifiConnected

    val pairedDevices: StateFlow<List<PairedDevice>> = app.deviceRepository.flow
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())

    val clipboardItems: StateFlow<List<ClipItem>> = app.clipboardBuffer.flow
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())

    val settings: StateFlow<Settings> = app.settingsRepository.flow
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), Settings())

    fun unpairDevice(apiKey: String) {
        viewModelScope.launch {
            app.deviceRepository.delete(apiKey)
        }
    }

    fun removeClip(item: ClipItem) {
        ClipImageCache.delete(app, item)
        app.clipboardBuffer.remove(item)
    }

    fun clearClips() {
        ClipImageCache.clear(app)
        app.clipboardBuffer.clear()
    }

    fun refreshDevices() {
        viewModelScope.launch {
            app.deviceRepository.refreshFlow()
        }
    }

    fun updatePort(port: Int) {
        viewModelScope.launch {
            app.settingsRepository.updatePort(port)
        }
    }

    fun updateDeviceName(name: String) {
        viewModelScope.launch {
            app.settingsRepository.updateDeviceName(name)
        }
    }

    fun regenerateDeviceName() {
        viewModelScope.launch {
            app.settingsRepository.updateDeviceName(DeviceNameGenerator.generate())
        }
    }

    companion object {
        fun getDeviceIp(): String? {
            return try {
                NetworkInterface.getNetworkInterfaces()?.asSequence()
                    ?.flatMap { it.inetAddresses.asSequence() }
                    ?.firstOrNull { !it.isLoopbackAddress && it is Inet4Address }
                    ?.hostAddress
            } catch (_: Exception) {
                null
            }
        }
    }
}
