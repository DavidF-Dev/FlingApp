package dev.davidfdev.fling.data

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.io.File

class DeviceRepository(private val file: File) {

    private val mutex = Mutex()
    private val json = Json { prettyPrint = true }

    suspend fun getAll(): List<PairedDevice> = mutex.withLock {
        readFile()
    }

    suspend fun findByKey(apiKey: String): PairedDevice? = mutex.withLock {
        readFile().find { it.apiKey == apiKey }
    }

    suspend fun store(device: PairedDevice) = mutex.withLock {
        val devices = readFile().toMutableList()
        val existing = devices.indexOfFirst { it.apiKey == device.apiKey }
        if (existing >= 0) {
            devices[existing] = device
        } else {
            devices.add(device)
        }
        writeFile(devices)
    }

    suspend fun delete(apiKey: String) = mutex.withLock {
        val devices = readFile().filter { it.apiKey != apiKey }
        writeFile(devices)
    }

    private suspend fun readFile(): List<PairedDevice> = withContext(Dispatchers.IO) {
        if (!file.exists()) return@withContext emptyList()
        val text = file.readText()
        if (text.isBlank()) return@withContext emptyList()
        json.decodeFromString<List<PairedDevice>>(text)
    }

    private suspend fun writeFile(devices: List<PairedDevice>) = withContext(Dispatchers.IO) {
        file.writeText(json.encodeToString(devices))
    }
}
