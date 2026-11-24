# Office encryption tests

Tests of `ThixOffice.Core/Formats/OfficeCrypto` ("Mit Kennwort verschlüsseln", [MS-OFFCRYPTO] / ECMA-376 Part 2, and the
compound file format [MS-CFB]).

- `ReferenceCrypto` is a second, plain implementation of Agile and Standard encryption written only for the tests (no code shared
  with the product). The product's output is decrypted by it, and files it encrypts (other hashes, key sizes, spin counts,
  with and without `dataIntegrity`, Standard AES-128/192/256) are decrypted by the product.
- `HandBuiltCompoundFile` lays out a version 4 compound file byte for byte, without the product's writer; the damaged-file
  tests change its FAT and directory.
- The published examples of [MS-OFFCRYPTO] are not used: their values could not be reproduced here with certainty.

**Still missing:** real files encrypted by Microsoft Word, Excel and PowerPoint (Agile from Office 2010 or later, Standard from
Office 2007) as fixtures, and a check that Office opens the files `OfficeEncryption.Encrypt` writes. The next step adds them as
soon as such files are available (with their passwords).

## OpenDocument encryption

Tests of `OdfEncryption` (ODF 1.2 Part 3, section 3.4: LibreOffice's "Mit Kennwort speichern").

- `ReferenceOdfCrypto` is a second, plain implementation written only for the tests (XmlDocument + XPath, PBKDF2 spelled out with
  HMACSHA1 and checked against the RFC 6070 vectors, ICryptoTransform, W3C padding with random filler). It decrypts what the product
  writes, and builds packages for the product to open: LibreOffice's current variant (sha256-1k, SHA-256 start key, AES-256, 100,000
  iterations) and older ones (SHA1/1K checksum, SHA-1 start key or none, AES-128, a part left unencrypted, no checksum).
- Blowfish, AES-GCM, Argon2id and LibreOffice 24.2's whole-package encryption ("encrypted-package") are only recognized and refused.

**Still missing:** real files encrypted by LibreOffice (LibreOffice is not installed here) as fixtures – one per variant (LibreOffice 3.4–7.x
with AES/sha256-1k, an OpenOffice.org file with Blowfish, LibreOffice 24.2+ with its newer encryption) – and a check that LibreOffice opens
the files `OdfEncryption.Encrypt` writes.
