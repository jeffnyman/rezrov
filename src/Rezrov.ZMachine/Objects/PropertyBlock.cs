namespace Rezrov.ZMachine.Objects;

/// <summary>
/// One property in an object's property table: its number, where its
/// data starts, and how many bytes of data it has.
/// </summary>
/// <remarks>
/// [zm 12.4] Properties are listed in an object's table in descending
/// order of number, and that order is a rule rather than a convention.
/// The size and number are packed into one or two bytes ahead of the
/// data, in a layout that differs between Versions 1 to 3 and Version 4
/// onward; <see cref="ObjectTable"/> does the unpacking and hands back
/// one of these.
/// </remarks>
/// <param name="Number">The property number, 1 upward.</param>
/// <param name="DataAddress">
/// The byte address of the first data byte.
/// </param>
/// <param name="Length">The number of data bytes.</param>
public readonly record struct PropertyBlock(int Number, int DataAddress, int Length);
