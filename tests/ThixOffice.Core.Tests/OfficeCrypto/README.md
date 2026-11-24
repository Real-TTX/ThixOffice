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
