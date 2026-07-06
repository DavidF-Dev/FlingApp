package dev.davidfdev.fling

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import androidx.core.content.ContextCompat
import kotlinx.coroutines.runBlocking

class BootReceiver : BroadcastReceiver() {

    override fun onReceive(context: Context, intent: Intent?) {
        if (intent?.action != Intent.ACTION_BOOT_COMPLETED) return
        val app = context.applicationContext as FlingApplication
        val enabled = runBlocking { app.settingsRepository.get().serviceEnabled }
        if (enabled) {
            ContextCompat.startForegroundService(
                context,
                Intent(context, FlingService::class.java),
            )
        }
    }
}
