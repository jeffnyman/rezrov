namespace Rezrov.ZMachine.Execution;

/// <summary>
/// A word of dynamic memory something was watching, and what the game
/// just did to it.
/// </summary>
/// <remarks>
/// [zm 6.2] The useful case is a global: which routine set the lamp
/// going out, or moved the thief, is a question about a value rather
/// than about an address, and the way to answer it is to watch the
/// value and let the game say where.
/// </remarks>
/// <param name="Address">The word that changed.</param>
/// <param name="Was">What it held.</param>
/// <param name="Now">What it holds.</param>
public readonly record struct Disturbance(int Address, ushort Was, ushort Now);
