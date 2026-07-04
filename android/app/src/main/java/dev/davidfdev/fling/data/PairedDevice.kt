package dev.davidfdev.fling.data

import kotlinx.serialization.Serializable

@Serializable
data class PairedDevice(
    val name: String,
    val apiKey: String,
    val pairedAt: Long,
)
