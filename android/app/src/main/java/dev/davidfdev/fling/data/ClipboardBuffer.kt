package dev.davidfdev.fling.data

import java.util.LinkedList

class ClipboardBuffer(private val capacity: Int = 10) {

    private val items = LinkedList<ClipItem>()
    private val lock = Any()

    fun add(item: ClipItem) = synchronized(lock) {
        items.addFirst(item)
        while (items.size > capacity) {
            items.removeLast()
        }
    }

    fun getAll(): List<ClipItem> = synchronized(lock) {
        items.toList()
    }

    fun size(): Int = synchronized(lock) {
        items.size
    }
}
