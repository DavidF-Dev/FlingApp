package dev.davidfdev.fling.ui

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import dev.davidfdev.fling.FlingApplication
import dev.davidfdev.fling.data.ClipItem
import dev.davidfdev.fling.data.PairedDevice
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import java.net.Inet4Address
import java.net.NetworkInterface

class MainViewModel(application: Application) : AndroidViewModel(application) {

    private val app = application as FlingApplication

    val serviceRunning: StateFlow<Boolean> = app.serviceRunning

    val pairedDevices: StateFlow<List<PairedDevice>> = app.deviceRepository.flow
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())

    val clipboardItems: StateFlow<List<ClipItem>> = app.clipboardBuffer.flow
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())

    fun unpairDevice(apiKey: String) {
        viewModelScope.launch {
            app.deviceRepository.delete(apiKey)
        }
    }

    fun refreshDevices() {
        viewModelScope.launch {
            app.deviceRepository.refreshFlow()
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
