package dev.davidfdev.fling.data

object DeviceNameGenerator {

    private val adjectives = listOf(
        "Amber", "Bold", "Bright", "Calm", "Clever",
        "Cool", "Coral", "Cozy", "Crisp", "Dapper",
        "Eager", "Fair", "Fast", "Fiery", "Fresh",
        "Gentle", "Golden", "Grand", "Happy", "Keen",
        "Kind", "Lively", "Lucky", "Merry", "Mighty",
        "Neat", "Noble", "Plucky", "Proud", "Quick",
        "Quiet", "Rapid", "Rosy", "Ruby", "Sandy",
        "Sharp", "Shiny", "Silver", "Sleek", "Smart",
        "Snowy", "Soft", "Solar", "Spicy", "Steady",
        "Swift", "Tidy", "Vivid", "Warm", "Witty",
    )

    private val nouns = listOf(
        "Acorn", "Arrow", "Beacon", "Birch", "Breeze",
        "Brook", "Cedar", "Cloud", "Comet", "Coral",
        "Crane", "Creek", "Daisy", "Dune", "Eagle",
        "Ember", "Fern", "Finch", "Flame", "Flint",
        "Fox", "Frost", "Grove", "Harbor", "Hawk",
        "Heron", "Ivy", "Jade", "Lake", "Lark",
        "Leaf", "Maple", "Marsh", "Moon", "Oak",
        "Otter", "Panda", "Pearl", "Pebble", "Pine",
        "Plum", "Raven", "Reed", "Ridge", "Robin",
        "Sage", "Sky", "Stone", "Tiger", "Willow",
    )

    fun generate(): String {
        val adj = adjectives.random()
        val noun = nouns.random()
        return "$adj $noun"
    }
}
