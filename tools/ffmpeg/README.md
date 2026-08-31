# tools/ffmpeg

Ce dossier accueille le binaire `ffmpeg.exe` embarqué par Videotoy. Il n'est
pas versionné (voir `.gitignore`) en raison de sa taille ; seuls `.gitkeep`,
`generate-hash.ps1` et `ffmpeg.exe.sha256` sont suivis par Git.

## Mise en place

1. Télécharger une build Windows 64 bits de FFmpeg (release statique,
   licence GPL/LGPL selon la build choisie), par exemple depuis
   https://www.gyan.dev/ffmpeg/builds/ (archive "release essentials" ou
   "release full").
2. Copier `ffmpeg.exe` extrait de l'archive directement dans ce dossier :
   `tools/ffmpeg/ffmpeg.exe`.
3. Générer le hash d'intégrité attendu par l'application au démarrage :

   ```
   powershell -ExecutionPolicy Bypass -File tools/ffmpeg/generate-hash.ps1
   ```

   Cela produit/actualise `tools/ffmpeg/ffmpeg.exe.sha256`.

## Build et installateur

- `Videotoy.App.csproj` copie automatiquement `ffmpeg.exe` et
  `ffmpeg.exe.sha256` dans le dossier de sortie du build
  (`bin/Release/.../tools/ffmpeg/`) s'ils sont présents ici.
- `installer/Videotoy.iss` embarque tout le dossier de sortie du build
  (donc `tools/ffmpeg/ffmpeg.exe` inclus) et refuse de compiler
  l'installateur si ce fichier est absent du build Release.
- `FfmpegIntegrityVerifier` vérifie au démarrage de l'application que le
  SHA-256 de `ffmpeg.exe` correspond à celui de `ffmpeg.exe.sha256`, afin de
  détecter toute corruption ou altération du binaire embarqué.

Penser à régénérer `ffmpeg.exe.sha256` (étape 3) à chaque mise à jour du
binaire `ffmpeg.exe`.
