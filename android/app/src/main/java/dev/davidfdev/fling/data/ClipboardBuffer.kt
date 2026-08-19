package dev.davidfdev.fling.data

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.util.LinkedList

class ClipboardBuffer(private val capacity: Int = 10) {

    private val items = LinkedList<ClipItem>()
    private val lock = Any()
    private val _flow = MutableStateFlow<List<ClipItem>>(emptyList())
    val flow: StateFlow<List<ClipItem>> = _flow.asStateFlow()

    fun add(item: ClipItem) = synchronized(lock) {
        items.addFirst(item)
        while (items.size > capacity) {
            items.removeLast()
        }
        _flow.value = items.toList()
    }

    fun remove(item: ClipItem) = synchronized(lock) {
        items.removeAll { it.id == item.id }
        _flow.value = items.toList()
    }

    fun clear() = synchronized(lock) {
        items.clear()
        _flow.value = emptyList()
    }

    fun getAll(): List<ClipItem> = synchronized(lock) {
        items.toList()
    }

    fun size(): Int = synchronized(lock) {
        items.size
    }
}
