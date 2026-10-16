using System;

namespace OpenArmada.Formats;

// A reader throws this for an asset whose format is known but deliberately not read. The asset
// check lists it apart and does not fail on it. Sealed, so a corrupt asset can never match it by
// being some other exception's subclass.
public sealed class UnsupportedFormatException(string message) : Exception(message);
