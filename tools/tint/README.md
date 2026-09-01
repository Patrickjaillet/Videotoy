# tools/tint

Ce dossier accueille le binaire `tint.exe` (Tint, le frontend/backend WGSL
du projet Dawn) utilisé pour transpiler les shaders `.wgsl` vers HLSL. Il
n'est pas versionné (voir `.gitignore`) en raison de sa taille ; seuls
`.gitkeep`, `generate-hash.ps1` et `tint.exe.sha256` sont suivis par Git.

**Le support WGSL est entièrement optionnel** : contrairement à
`tools/ffmpeg/ffmpeg.exe` (requis pour tout export), l'absence de
`tint.exe` n'empêche jamais l'application de démarrer ni de fonctionner —
seul le chargement d'un fichier `.wgsl` échoue alors, avec un message
explicite dans le panneau « Shader Issues ». GLSL et HLSL natif restent
pleinement fonctionnels sans ce binaire.

## Mise en place

1. Obtenir une build Windows 64 bits de Tint (voir le projet Dawn/Chromium,
   licence BSD-3-Clause), fournissant un exécutable `tint.exe` capable de
   convertir un fichier `.wgsl` en HLSL (`--format hlsl` ou équivalent —
   **hypothèse à vérifier au moment de récupérer le binaire** : les flags
   exacts de la CLI Tint doivent être confirmés via `tint.exe --help`,
   voir `WgslTranspilerProcess.cs` où l'invocation exacte est isolée pour
   rester facile à corriger si cette hypothèse s'avère fausse).
2. Copier `tint.exe` directement dans ce dossier : `tools/tint/tint.exe`.
3. Générer le hash d'intégrité :

   ```
   powershell -ExecutionPolicy Bypass -File tools/tint/generate-hash.ps1
   ```

   Cela produit/actualise `tools/tint/tint.exe.sha256`.

## Build et installateur

- `Videotoy.App.csproj` copie automatiquement `tint.exe` et
  `tint.exe.sha256` dans le dossier de sortie du build s'ils sont présents
  ici, exactement comme pour `ffmpeg.exe`.
- `installer/Videotoy.iss` n'exige PAS la présence de `tint.exe` (contrairement à
  `ffmpeg.exe`) : un build/installateur sans support WGSL est un scénario
  valide, packagé silencieusement.
- `TintIntegrityVerifier` (`Videotoy.Transpiler`) vérifie le SHA-256 de
  manière paresseuse, au premier chargement d'un fichier `.wgsl` — jamais
  au démarrage de l'application.

Penser à régénérer `tint.exe.sha256` (étape 3) à chaque mise à jour du
binaire `tint.exe`.
