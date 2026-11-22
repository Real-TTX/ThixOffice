param([string]$Out)
# Generates the 12 fixture mails of ThixMail (tests/ThixMail.Core.Tests/Fixtures). Run once; the files are committed.
New-Item -ItemType Directory -Force $Out | Out-Null
Add-Type -AssemblyName System.Drawing

function Crlf([string]$text) { return ($text -replace "`r`n", "`n" -replace "`n", "`r`n") }
function Write-Ascii([string]$name, [string]$text) { [IO.File]::WriteAllBytes((Join-Path $Out $name), [Text.Encoding]::ASCII.GetBytes((Crlf $text))) }
function Write-Bytes([string]$name, [byte[]]$bytes) { [IO.File]::WriteAllBytes((Join-Path $Out $name), $bytes) }
function B64([byte[]]$bytes) { $s = [Convert]::ToBase64String($bytes); return (($s -split '(.{76})' | Where-Object { $_ }) -join "`r`n") }
function Utf8B64([string]$text) { return B64 ([Text.Encoding]::UTF8.GetBytes((Crlf $text))) }
function Png([int]$w, [int]$h, [string]$color, [string]$label) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.ColorTranslator]::FromHtml($color))
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $font = New-Object System.Drawing.Font "Arial", ([Math]::Max(8, [int]($h / 3)))
    $g.DrawString($label, $font, [System.Drawing.Brushes]::White, 6, [int]($h / 4))
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    return $ms.ToArray()
}
$pdf = [Text.Encoding]::ASCII.GetBytes("%PDF-1.4`n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj 2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj 3 0 obj<</Type/Page/MediaBox[0 0 595 842]/Parent 2 0 R>>endobj`ntrailer<</Root 1 0 R>>`n%%EOF`n")

# 01 plain text, UTF-8, quoted-printable
Write-Ascii "01-plain.eml" @"
From: Jonas Weber <jonas.weber@example.de>
To: Anna Berger <anna@example.de>
Subject: =?UTF-8?Q?Termin_f=C3=BCr_die_Besprechung?=
Date: Mon, 16 Nov 2026 09:12:00 +0100
Message-ID: <plain-01@example.de>
MIME-Version: 1.0
Content-Type: text/plain; charset=utf-8; format=flowed
Content-Transfer-Encoding: quoted-printable

Hallo Anna,

passt dir Donnerstag um 14 Uhr f=C3=BCr die Besprechung zum Projekt Nordlich=
t? Die Unterlagen liegen unter https://example.de/projekte/nordlicht bereit.

Gr=C3=BC=C3=9Fe aus K=C3=B6ln
Jonas

--=20
Jonas Weber =C2=B7 Projektleitung
Beispiel GmbH =C2=B7 Tel. 0221 123456
"@

# 02 simple HTML with a plain alternative
Write-Ascii "02-simple-html.eml" @"
From: Lena Koch <lena.koch@example.org>
To: anna@example.de
Subject: Einladung zum Sommerfest
Date: Tue, 17 Nov 2026 16:40:00 +0100
Message-ID: <html-02@example.org>
MIME-Version: 1.0
Content-Type: multipart/alternative; boundary="alt02"

--alt02
Content-Type: text/plain; charset=utf-8
Content-Transfer-Encoding: base64

$(Utf8B64 "Liebe Kolleginnen und Kollegen,`n`nam Freitag feiern wir unser Sommerfest.`n- Beginn 17 Uhr`n- Grillen im Hof`n- Musik ab 19 Uhr`n`nAnmeldung: https://example.org/fest`n`nLena")
--alt02
Content-Type: text/html; charset=utf-8
Content-Transfer-Encoding: base64

$(Utf8B64 "<html><body style=""font-family:Segoe UI,Arial;font-size:14px""><p>Liebe Kolleginnen und Kollegen,</p><p>am <b>Freitag</b> feiern wir unser <span style=""color:#C43E1C"">Sommerfest</span>.</p><ul><li>Beginn 17&nbsp;Uhr</li><li>Grillen im Hof</li><li>Musik ab 19&nbsp;Uhr</li></ul><p>Bitte meldet euch <a href=""https://example.org/fest"">hier an</a>.</p><p>Viele Grüße<br><i>Lena</i></p></body></html>")
--alt02--
"@

# 03 newsletter: nested layout tables, colours, remote pictures, hidden preheader, a tracking pixel
$news = @"
<!DOCTYPE html><html><head><meta charset="utf-8"><style>.btn{background:#0F6CBD}</style><title>Newsletter</title></head>
<body style="margin:0;background:#EEEEEE">
<div style="display:none;max-height:0;overflow:hidden">Die Neuheiten im November – jetzt lesen</div>
<table width="100%" cellpadding="0" cellspacing="0" bgcolor="#EEEEEE"><tr><td align="center">
 <table width="600" cellpadding="0" cellspacing="0" bgcolor="#FFFFFF">
  <tr><td bgcolor="#1F3B57" style="padding:20px"><img src="https://news.example.com/img/logo.png" width="160" height="40" alt="Beispiel Shop"></td></tr>
  <tr><td style="padding:24px"><h1 style="font-family:Georgia;color:#1F3B57">Neu im November</h1>
   <p style="font-size:15px;color:#333333">Entdecken Sie unsere Herbstkollektion – nur für kurze Zeit <b>20&nbsp;% günstiger</b>.</p>
   <table width="100%" cellpadding="8"><tr>
     <td width="50%" valign="top"><img src="https://news.example.com/img/jacke.jpg" width="260" height="180" alt="Jacke"><p><b>Wetterjacke</b><br>89,00&nbsp;€</p></td>
     <td width="50%" valign="top"><img src="https://news.example.com/img/schal.jpg" width="260" height="180" alt="Schal"><p><b>Wollschal</b><br>29,00&nbsp;€</p></td>
   </tr></table>
   <table cellpadding="12" align="center"><tr><td bgcolor="#0F6CBD" style="border-radius:4px"><a href="https://news.example.com/shop?utm=nl" style="color:#FFFFFF;font-weight:bold;text-decoration:none">Jetzt entdecken</a></td></tr></table>
  </td></tr>
  <tr><td bgcolor="#F4F4F4" style="padding:16px;font-size:11px;color:#777777">Sie erhalten diesen Newsletter, weil Sie ihn bestellt haben. <a href="https://news.example.com/abmelden">Abmelden</a> · <a href="javascript:alert(1)">Profil</a></td></tr>
 </table>
 <img src="https://track.example.com/open.gif?id=42" width="1" height="1" alt="">
</td></tr></table></body></html>
"@
Write-Ascii "03-newsletter.eml" @"
From: Beispiel Shop <newsletter@news.example.com>
To: anna@example.de
Subject: Neu im November: 20 % auf die Herbstkollektion
Date: Wed, 18 Nov 2026 07:00:00 +0100
Message-ID: <nl-03@news.example.com>
List-Unsubscribe: <https://news.example.com/abmelden>
MIME-Version: 1.0
Content-Type: text/html; charset=utf-8
Content-Transfer-Encoding: base64

$(Utf8B64 $news)
"@

# 04 a reply chain with nested quotes
$reply = @"
<html><body><div dir="ltr">Prima, dann machen wir es so. Ich bringe den Beamer mit.<br><br>Anna</div><br>
<div class="gmail_quote"><div dir="ltr" class="gmail_attr">Am Mo., 16. Nov. 2026 um 09:12 Uhr schrieb Jonas Weber &lt;jonas.weber@example.de&gt;:<br></div>
<blockquote class="gmail_quote" style="margin:0 0 0 .8ex;border-left:1px #ccc solid;padding-left:1ex">Passt dir Donnerstag um 14 Uhr?<br><br>
<div class="gmail_quote">Am So., 15. Nov. 2026 schrieb Anna Berger:<br>
<blockquote class="gmail_quote" style="margin:0 0 0 .8ex;border-left:1px #ccc solid;padding-left:1ex">Wann können wir uns zum Projekt treffen?<br>Ich hätte diese Woche noch Zeit.</blockquote></div>
</blockquote></div></body></html>
"@
Write-Ascii "04-reply-chain.eml" @"
From: Anna Berger <anna@example.de>
To: Jonas Weber <jonas.weber@example.de>
Subject: AW: Termin =?UTF-8?Q?f=C3=BCr?= die Besprechung
Date: Mon, 16 Nov 2026 10:05:00 +0100
Message-ID: <reply-04@example.de>
In-Reply-To: <plain-01@example.de>
References: <start-00@example.de> <plain-01@example.de>
MIME-Version: 1.0
Content-Type: multipart/alternative; boundary="alt04"

--alt04
Content-Type: text/plain; charset=utf-8
Content-Transfer-Encoding: base64

$(Utf8B64 "Prima, dann machen wir es so. Ich bringe den Beamer mit.`n`nAnna`n`nAm Mo., 16. Nov. 2026 um 09:12 Uhr schrieb Jonas Weber:`n> Passt dir Donnerstag um 14 Uhr?`n>`n> Am So., 15. Nov. 2026 schrieb Anna Berger:`n>> Wann können wir uns zum Projekt treffen?`n>> Ich hätte diese Woche noch Zeit.")
--alt04
Content-Type: text/html; charset=utf-8
Content-Transfer-Encoding: base64

$(Utf8B64 $reply)
--alt04--
"@

# 05 an inline picture by Content-ID
$logo = Png 240 60 "#0F6CBD" "ThixMail"
Write-Ascii "05-inline-image.eml" @"
From: Marketing <marketing@example.net>
To: anna@example.de
Subject: Unser neues Logo
Date: Thu, 19 Nov 2026 11:30:00 +0100
Message-ID: <inline-05@example.net>
MIME-Version: 1.0
Content-Type: multipart/related; boundary="rel05"; type="text/html"

--rel05
Content-Type: text/html; charset=utf-8
Content-Transfer-Encoding: base64

$(Utf8B64 "<html><body><p>Hallo Anna,</p><p>so sieht unser neues Logo aus:</p><p><img src=""cid:logo05@example.net"" alt=""Logo""></p><p>Was meinst du?</p></body></html>")
--rel05
Content-Type: image/png; name="logo.png"
Content-Disposition: inline; filename="logo.png"
Content-ID: <logo05@example.net>
Content-Transfer-Encoding: base64

$(B64 $logo)
--rel05--
"@

# 06 attachments: a PDF, a large file, an executable
$large = New-Object byte[] (1536 * 1024)
for ($i = 0; $i -lt $large.Length; $i++) { $large[$i] = [byte](($i * 7) % 256) }
Write-Ascii "06-attachments.eml" @"
From: Buchhaltung <buchhaltung@example.com>
To: anna@example.de
Subject: Rechnung November und Unterlagen
Date: Fri, 20 Nov 2026 08:15:00 +0100
Message-ID: <att-06@example.com>
MIME-Version: 1.0
Content-Type: multipart/mixed; boundary="mix06"

--mix06
Content-Type: text/plain; charset=utf-8

Guten Tag Frau Berger,

anbei die Rechnung fuer November, die Projektdaten und das Programm zum Einlesen.

Mit freundlichen Gruessen
Ihre Buchhaltung
--mix06
Content-Type: application/pdf; name="Rechnung-2026-11.pdf"
Content-Disposition: attachment; filename="Rechnung-2026-11.pdf"
Content-Transfer-Encoding: base64

$(B64 $pdf)
--mix06
Content-Type: application/octet-stream; name="Projektdaten.bin"
Content-Disposition: attachment; filename="Projektdaten.bin"
Content-Transfer-Encoding: base64

$(B64 $large)
--mix06
Content-Type: application/x-msdownload; name="Einlesen.exe"
Content-Disposition: attachment; filename="Einlesen.exe"
Content-Transfer-Encoding: base64

$(B64 ([Text.Encoding]::ASCII.GetBytes("MZ not really a program")))
--mix06--
"@

# 07 a calendar invitation
$ics = Crlf @"
BEGIN:VCALENDAR
PRODID:-//Example//Kalender//DE
VERSION:2.0
METHOD:REQUEST
BEGIN:VEVENT
UID:termin-07@example.de
DTSTART:20261126T130000Z
DTEND:20261126T140000Z
SUMMARY:Projektbesprechung Nordlicht
LOCATION:Raum 2.14
ORGANIZER;CN=Jonas Weber:mailto:jonas.weber@example.de
ATTENDEE;CN=Anna Berger;RSVP=TRUE:mailto:anna@example.de
END:VEVENT
END:VCALENDAR
"@
Write-Ascii "07-calendar-invite.eml" @"
From: Jonas Weber <jonas.weber@example.de>
To: Anna Berger <anna@example.de>
Subject: Einladung: Projektbesprechung Nordlicht (Do. 26.11.2026 14:00)
Date: Fri, 20 Nov 2026 12:00:00 +0100
Message-ID: <cal-07@example.de>
MIME-Version: 1.0
Content-Type: multipart/mixed; boundary="mix07"

--mix07
Content-Type: multipart/alternative; boundary="alt07"

--alt07
Content-Type: text/plain; charset=utf-8

Projektbesprechung Nordlicht
Wann: Donnerstag, 26. November 2026, 14:00-15:00
Wo: Raum 2.14
--alt07
Content-Type: text/html; charset=utf-8

<html><body><h2>Projektbesprechung Nordlicht</h2><p><b>Wann:</b> Donnerstag, 26. November 2026, 14:00&ndash;15:00<br><b>Wo:</b> Raum 2.14</p></body></html>
--alt07
Content-Type: text/calendar; charset=utf-8; method=REQUEST

$ics
--alt07--
--mix07
Content-Type: application/ics; name="einladung.ics"
Content-Disposition: attachment; filename="einladung.ics"
Content-Transfer-Encoding: base64

$(B64 ([Text.Encoding]::UTF8.GetBytes($ics)))
--mix07--
"@

# 08 ISO-8859-1 text, an RFC 2047 subject in ISO-8859-1
$latin = [Text.Encoding]::GetEncoding("iso-8859-1")
$head08 = Crlf @"
From: =?ISO-8859-1?Q?J=FCrgen_M=FCller?= <juergen@example.at>
To: anna@example.de
Subject: =?ISO-8859-1?Q?Gr=FC=DFe_aus_=D6sterreich?=
Date: Sat, 21 Nov 2026 18:20:00 +0100
Message-ID: <latin-08@example.at>
MIME-Version: 1.0
Content-Type: text/plain; charset=ISO-8859-1
Content-Transfer-Encoding: 8bit


"@
Write-Bytes "08-iso-8859-1.eml" ($latin.GetBytes($head08) + $latin.GetBytes((Crlf "Liebe Anna,`n`nschöne Grüße aus Österreich! Die Mozartkugeln für Ihr Büro sind unterwegs.`nÜbermorgen ist auch die Rechnung über 45,50 EUR da.`n`nJürgen")))

# 09 Windows-1252 HTML, quoted-printable (curly quotes, euro sign, dashes)
$cp1252 = [Text.Encoding]::GetEncoding(1252)
$q1 = [string][char]0x201E; $q2 = [string][char]0x201C; $dash = [string][char]0x2013; $euro = [string][char]0x20AC
$body09 = $cp1252.GetBytes('<html><body><p>Sehr geehrte Frau Berger,</p><p>vielen Dank für Ihre Bestellung ' + $q1 + 'Premium' + $q2 + ' ' + $dash + ' der Betrag von 1.299,00 ' + $euro + ' wird am Montag abgebucht.</p><p>Freundliche Grüße</p></body></html>')
$qp = New-Object Text.StringBuilder
$line = 0
foreach ($b in $body09) {
    $piece = if (($b -ge 33 -and $b -le 126 -and $b -ne 61) -or $b -eq 32) { [string][char]$b } else { "=" + $b.ToString("X2") }
    if ($line + $piece.Length -gt 74) { [void]$qp.Append("=`r`n"); $line = 0 }
    [void]$qp.Append($piece); $line += $piece.Length
}
Write-Ascii "09-windows-1252-html.eml" @"
From: Versandhaus <service@example.shop>
To: anna@example.de
Subject: =?windows-1252?Q?Ihre_Bestellung_=84Premium=93?=
Date: Sun, 22 Nov 2026 08:00:00 +0100
Message-ID: <cp1252-09@example.shop>
MIME-Version: 1.0
Content-Type: text/html; charset="windows-1252"
Content-Transfer-Encoding: quoted-printable

$($qp.ToString())
"@

# 10 ISO-2022-JP (Japanese) and KOI8-R (Russian) parts
$jis = [Text.Encoding]::GetEncoding("iso-2022-jp")
$koi = [Text.Encoding]::GetEncoding("koi8-r")
$head10 = Crlf @"
From: Tanaka Yuki <yuki@example.jp>
To: anna@example.de
Subject: =?ISO-2022-JP?B?$([Convert]::ToBase64String($jis.GetBytes("会議の資料")))?=
Date: Sun, 22 Nov 2026 03:00:00 +0900
Message-ID: <jis-10@example.jp>
MIME-Version: 1.0
Content-Type: multipart/mixed; boundary="mix10"

--mix10
Content-Type: text/plain; charset=ISO-2022-JP
Content-Transfer-Encoding: 7bit


"@
$mid10 = Crlf "`n--mix10`nContent-Type: text/plain; charset=KOI8-R`nContent-Transfer-Encoding: 8bit`n`n"
$end10 = Crlf "`n--mix10--`n"
Write-Bytes "10-legacy-charsets.eml" ([Text.Encoding]::ASCII.GetBytes($head10) + $jis.GetBytes((Crlf "アンナさん`n会議の資料を送ります。`nよろしくお願いします。")) + [Text.Encoding]::ASCII.GetBytes($mid10) + $koi.GetBytes((Crlf "Привет, Анна! Документы во вложении.")) + [Text.Encoding]::ASCII.GetBytes($end10))

# 11 broken MIME: no closing boundary, bad base64, 8-bit bytes without a charset, a header without a colon
$broken = Crlf @"
From: Kaputt Mailer <broken@example.invalid>
To: anna@example.de
Subject: Defekte Nachricht
Date: Kein Datum
This line is not a header
MIME-Version: 1.0
Content-Type: multipart/mixed; boundary="never-closed"

--never-closed
Content-Type: text/plain

Dieser Teil hat Umlaute ohne Zeichensatz:
"@
Write-Bytes "11-broken-mime.eml" ([Text.Encoding]::ASCII.GetBytes($broken) + $latin.GetBytes("Grüße äöü") + [Text.Encoding]::ASCII.GetBytes((Crlf "`n--never-closed`nContent-Type: image/png; name=x.png`nContent-Transfer-Encoding: base64`n`n%%%% not base64 at all !!!!`n")))

# 12 Outlook HTML: Word classes, conditional comments, VML, a signature picture by cid, an RFC 2231 file name
$sig = Png 120 40 "#185ABD" "Beispiel"
$outlook = @"
<html xmlns:v="urn:schemas-microsoft-com:vml" xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:w="urn:schemas-microsoft-com:office:word"><head><meta http-equiv="Content-Type" content="text/html; charset=utf-8"><meta name="Generator" content="Microsoft Word 15 (filtered medium)"><!--[if !mso]><style>v\:* {behavior:url(#default#VML);}</style><![endif]--><style><!-- p.MsoNormal {margin:0cm;font-size:11.0pt;font-family:"Calibri",sans-serif;} --></style><!--[if gte mso 9]><xml><o:shapedefaults v:ext="edit" spidmax="1026" /></xml><![endif]--></head>
<body lang="DE" link="#0563C1" vlink="#954F72"><div class="WordSection1"><p class="MsoNormal">Hallo Frau Berger,<o:p></o:p></p><p class="MsoNormal"><o:p>&nbsp;</o:p></p>
<p class="MsoNormal">anbei die Präsentation für Donnerstag. Die wichtigsten Punkte:<o:p></o:p></p>
<ol style="margin-top:0cm" start="1" type="1"><li class="MsoNormal">Zeitplan bis März<o:p></o:p></li><li class="MsoNormal">Budget<o:p></o:p></li><li class="MsoNormal">Nächste Schritte<o:p></o:p></li></ol>
<p class="MsoNormal"><o:p>&nbsp;</o:p></p><p class="MsoNormal">Mit freundlichen Grüßen<o:p></o:p></p>
<table class="MsoNormalTable" border="0" cellspacing="0" cellpadding="0"><tr><td style="padding:0cm 8pt 0cm 0cm"><p class="MsoNormal"><img width="120" height="40" style="width:1.25in;height:.4166in" id="Bild_x0020_1" src="cid:image001.png@01DB3C2A.5F1E7A00" alt="Beispiel GmbH"></p></td>
<td style="border:none;border-left:solid #185ABD 1.5pt;padding:0cm 0cm 0cm 8pt"><p class="MsoNormal"><b><span style="color:#185ABD">Maria Schulz</span></b><br><span style="font-size:9.0pt;color:gray">Vertrieb · Beispiel GmbH<br>Tel. +49 30 987654</span><o:p></o:p></p></td></tr></table></div></body></html>
"@
Write-Ascii "12-outlook-html.eml" @"
From: "Schulz, Maria" <maria.schulz@example.com>
To: "Berger, Anna" <anna@example.de>
Subject: =?utf-8?B?$([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("Präsentation für Donnerstag")))?=
Date: Sun, 22 Nov 2026 10:45:00 +0100
Message-ID: <0a1b2c3d@EXAMPLE.COM>
Thread-Topic: =?utf-8?B?$([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("Präsentation für Donnerstag")))?=
X-MS-Has-Attach: yes
MIME-Version: 1.0
Content-Type: multipart/mixed; boundary="_004_outlook_"

--_004_outlook_
Content-Type: multipart/related; boundary="_003_outlook_"; type="multipart/alternative"

--_003_outlook_
Content-Type: multipart/alternative; boundary="_000_outlook_"

--_000_outlook_
Content-Type: text/plain; charset="utf-8"
Content-Transfer-Encoding: base64

$(Utf8B64 "Hallo Frau Berger,`n`nanbei die Präsentation für Donnerstag. Die wichtigsten Punkte:`n1. Zeitplan bis März`n2. Budget`n3. Nächste Schritte`n`nMit freundlichen Grüßen`nMaria Schulz")
--_000_outlook_
Content-Type: text/html; charset="utf-8"
Content-Transfer-Encoding: base64

$(Utf8B64 $outlook)
--_000_outlook_--
--_003_outlook_
Content-Type: image/png; name="image001.png"
Content-Description: image001.png
Content-Disposition: inline; filename="image001.png"
Content-ID: <image001.png@01DB3C2A.5F1E7A00>
Content-Transfer-Encoding: base64

$(B64 $sig)
--_003_outlook_--
--_004_outlook_
Content-Type: application/pdf; name*=utf-8''Pr%C3%A4sentation%20%C3%9Cbersicht.pdf
Content-Disposition: attachment; filename*=utf-8''Pr%C3%A4sentation%20%C3%9Cbersicht.pdf
Content-Transfer-Encoding: base64

$(B64 $pdf)
--_004_outlook_--
"@
Get-ChildItem $Out | ForEach-Object { "{0,10} {1}" -f $_.Length, $_.Name }
