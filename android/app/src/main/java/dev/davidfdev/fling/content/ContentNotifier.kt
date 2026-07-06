package dev.davidfdev.fling.content

import dev.davidfdev.fling.data.ClipItem

interface ContentNotifier {
    fun notify(item: ClipItem)
}
