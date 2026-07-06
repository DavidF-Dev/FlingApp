package dev.davidfdev.fling.data

import android.content.Context
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.intPreferencesKey
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map

data class Settings(
    val port: Int = 7291,
    val deviceName: String = "",
    val serviceEnabled: Boolean = false,
)

private val Context.dataStore by preferencesDataStore(name = "settings")

class SettingsRepository(private val context: Context) {

    private val portKey = intPreferencesKey("port")
    private val deviceNameKey = stringPreferencesKey("device_name")
    private val serviceEnabledKey = booleanPreferencesKey("service_enabled")

    val flow: Flow<Settings> = context.dataStore.data.map { prefs ->
        Settings(
            port = prefs[portKey] ?: 7291,
            deviceName = prefs[deviceNameKey] ?: "",
            serviceEnabled = prefs[serviceEnabledKey] ?: false,
        )
    }

    suspend fun get(): Settings = flow.first()

    suspend fun updatePort(port: Int) {
        require(port in 1..65535) { "Port must be between 1 and 65535" }
        context.dataStore.edit { it[portKey] = port }
    }

    suspend fun updateDeviceName(name: String) {
        require(name.isNotBlank()) { "Device name must not be blank" }
        context.dataStore.edit { it[deviceNameKey] = name.trim() }
    }

    suspend fun updateServiceEnabled(enabled: Boolean) {
        context.dataStore.edit { it[serviceEnabledKey] = enabled }
    }

    suspend fun initializeDefaults() {
        val current = get()
        if (current.deviceName.isBlank()) {
            updateDeviceName(DeviceNameGenerator.generate())
        }
    }
}
