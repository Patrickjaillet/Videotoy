# Roadmap — Corrections à apporter

Liste des problèmes identifiés lors d'une revue de code du dépôt (rendu Direct3D 11,
pipeline d'export FFmpeg, transpileur de shaders, application WPF). Classés par
catégorie et par sévérité. État après la passe de correction du
24/09/2026 (voir notes `>` sous chaque item corrigé).

## Critique

- [x] **FFmpeg : fuite de `Process` si `Process.Start()` échoue.** Dans
  `src/Videotoy.Ffmpeg/FfmpegService.cs` (`LaunchProcessAsync`, ~L237-266), si
  `Start()` lève une exception (ex. `ffmpeg.exe` manquant ou bloqué), `_process`
  reste non-null mais n'a jamais démarré. `IsRunning` (L40) appelle alors
  `HasExited` sur un process non démarré, ce qui lève `InvalidOperationException`
  et bloque définitivement le service (tous les exports suivants échouent jusqu'au
  redémarrage de l'appli). Il faut englober `Start()` dans un try/catch qui
  réinitialise `_process`/`_stdin` en cas d'échec.
  > Corrigé : `_process` n'est assigné qu'après un `Start()` réussi ; en cas
  > d'échec le `Process` est disposé et l'exception propage sans laisser
  > `IsRunning` dans un état incohérent.

- [x] **Cache vidéo partagé ignorant la résolution → corruption mémoire GPU.**
  `src/Videotoy.Ffmpeg/VideoFrameKey.cs` (L13) définit la clé de cache comme
  `(VideoFilePath, FrameIndex)` sans la résolution cible. Le cache
  (`VideoTextureLoader.cs` L43-57) est un singleton DI partagé entre l'aperçu
  (basse résolution) et l'export (résolution potentiellement 4K). Une frame
  décodée en aperçu peut être réutilisée telle quelle lors d'un export à une
  résolution différente, et `MultiPassRenderer.RefreshDynamicAssets` (L588-620)
  calcule `rowSizeInBytes` à partir de ces dimensions erronées avant une copie
  mémoire non sécurisée (`Buffer.MemoryCopy`) vers une texture D3D11 mappée —
  risque réel de corruption/débordement mémoire, reproductible simplement en
  prévisualisant puis en exportant un shader utilisant une texture vidéo.
  > Corrigé : `VideoFrameKey` inclut désormais `TargetWidth`/`TargetHeight` ;
  > deux résolutions différentes du même fichier/frame n'entrent plus en
  > collision dans le cache (tests de régression ajoutés).

## Élevé

- [x] **Erreur "disque plein" reclassée à tort comme transitoire.**
  `src/Videotoy.Ffmpeg/TransientFfmpegErrorClassifier.cs` (L13-20) traite toute
  `IOException` comme transitoire, alors que le commentaire de la classe indique
  explicitement qu'un disque plein ne doit jamais être retenté. Un disque plein
  remonte comme `IOException` (pipe brisé) depuis `FfmpegService.WriteFrameAsync`
  avant que `FfmpegStderrDiagnosis` puisse le classifier correctement, ce qui fait
  relancer inutilement tout le pipeline de rendu (`VideoExportPipeline.RunAsync`,
  jusqu'à `MaxTransientRetries` fois) pour un export voué à l'échec.
  > Corrigé : `WriteFrameAsync`/`FinishAsync` traduisent désormais une rupture
  > de pipe causée par la fin du process en `FfmpegEncodingException`
  > diagnostiquée (stderr réellement analysé) plutôt qu'une `IOException`
  > brute ; le classifieur ne traite plus jamais une `IOException` comme
  > transitoire (tests de régression ajoutés).

- [x] **Vérification d'intégrité FFmpeg (SHA-256) faite une seule fois, TOCTOU.**
  `src/Videotoy.Ffmpeg/FfmpegIntegrityVerifier.cs` (L14-40) n'est appelée qu'au
  démarrage (`App.xaml.cs` L42-58). Aucun des nombreux appels ultérieurs à
  `Process.Start()` (export, décodage vidéo, sonde matérielle) ne revérifie le
  binaire. Un remplacement du fichier après le démarrage (mise à jour ratée,
  logiciel malveillant, antivirus) passerait inaperçu.
  > Corrigé : nouvelle méthode `EnsureStillValid()` appelée avant chaque
  > lancement de `ffmpeg.exe` (`FfmpegService`, `VideoFrameDecoder`,
  > `VideoProber`, `HardwareEncoderProbe`) ; ne recalcule le SHA-256 complet
  > que si la date de modification ou la taille du fichier a changé depuis la
  > dernière vérification, pour rester bon marché sur un export long.

- [ ] **Flags CLI de `tint.exe` non vérifiés (TODO explicite de l'auteur).**
  `src/Videotoy.Transpiler/WgslTranspilerProcess.cs` (L47-55) contient un TODO
  indiquant que les flags (`--format hlsl -o ...`) n'ont jamais été vérifiés
  contre le binaire réel qui sera distribué. Risque d'échec silencieux ou de
  comportement différent selon la version de `tint.exe` embarquée — à tester
  contre le binaire réellement livré dans `tools/tint/`.
  > Non vérifiable dans cet environnement (pas de binaire `tint.exe`, pas de
  > Windows/SDK disponibles pour l'exécuter). L'invocation actuelle
  > correspond à la CLI documentée du projet Dawn/Tint et n'a donc pas été
  > changée ; commentaire mis à jour pour expliquer où regarder en premier si
  > le chargement WGSL échoue systématiquement une fois le binaire en place.
  > **Reste à faire manuellement** : lancer `tint.exe --help` avec le binaire
  > réel et confirmer les flags avant la première release avec support WGSL.

- [x] **`iMouse`, `iDate` et `iChannelResolution` toujours à zéro.**
  `src/Videotoy.Rendering/D3D11ShaderRenderer.cs` (L122-138) et
  `MultiPassRenderer.cs` (L631-647, code dupliqué) fixent ces uniformes
  Shadertoy standards à `Vector4.Zero`. Tout shader importé qui utilise
  l'interaction souris, la date, ou la résolution par canal (usages très
  courants sur Shadertoy) rendra un résultat visuellement incorrect, sans
  aucun diagnostic pour l'utilisateur.
  > `iChannelResolution0-3` corrigé : calculé pour de vrai par passe à partir
  > des textures effectivement liées (buffer d'une autre passe ou asset
  > image/vidéo/spectre audio). `iMouse`/`iDate` restent **volontairement**
  > à zéro : ce sont les deux seuls uniformes Shadertoy intrinsèquement non
  > déterministes (horloge murale, interaction temps réel), et ce projet
  > garantit explicitement un pipeline de rendu déterministe — les rendre
  > réels romprait cette garantie (deux exports du même shader à des
  > instants différents produiraient des vidéos différentes). Voir le
  > commentaire ajouté dans `MultiPassRenderer.UpdateUniforms`.
  > Le renderer mono-passe dupliqué (`D3D11ShaderRenderer`/`NullShaderRenderer`/
  > `IShaderRenderer`), confirmé mort (item Faible ci-dessous), a été
  > supprimé plutôt que corrigé en double.

- [x] **Boucle d'export de la file de rendu exécutée sur le thread UI.**
  `src/Videotoy.App/ViewModels/MainWindowViewModel.cs` (`StartRenderQueueAsync`,
  ~L1577-1593) appelle `_renderQueueProcessor.StartAsync` sans `Task.Run`, et
  aucun `ConfigureAwait(false)` n'est utilisé dans toute la chaîne d'appels
  (`RenderQueueProcessor.cs` L245-310, `VideoExportPipeline.cs` L138-140). Le
  rendu D3D11 et la lecture des pixels (`Map` bloquant) s'exécutent donc entre
  les `await` sur le thread UI, provoquant des blocages/saccades pendant toute
  la durée d'un export par lot, surtout en haute résolution.
  > Corrigé à la racine : `FrameSequenceRenderer.RenderSequence` est devenu un
  > `IAsyncEnumerable` où chaque rendu de frame passe par `Task.Run`,
  > garantissant qu'il ne s'exécute jamais en ligne sur l'appelant — y
  > compris quand un `await` voisin se termine de façon synchrone et ne cède
  > donc pas la main de lui-même. Les trois pipelines d'export (vidéo, image
  > animée, séquence d'images) mis à jour en conséquence.

- [x] **Aucun gestionnaire global d'exceptions non gérées.** Recherche sur tout
  le dépôt : ni `DispatcherUnhandledException`, ni
  `AppDomain.CurrentDomain.UnhandledException`, ni
  `TaskScheduler.UnobservedTaskException` (`src/Videotoy.App`). Combiné aux
  `async void` non protégés (voir plus bas), toute exception échappée plante
  l'application entière sans message ni log de crash.
  > Corrigé : les trois gestionnaires ajoutés dans `App()`, avec message
  > utilisateur clair et trace (`System.Diagnostics.Trace`) au lieu d'un
  > plantage silencieux.

- [x] **Aucun projet de tests dans la solution.** `Videotoy.sln` ne référence
  que les 6 projets applicatifs, aucun projet de test. Zéro couverture
  automatisée pour la logique F# (`ShadertoyJsonParser`, `ShaderValidator`,
  `PassGraph`, `LoopCalculator`), la construction des arguments FFmpeg
  (`FfmpegService.BuildArguments`), `FfmpegStderrParser` /
  `TransientFfmpegErrorClassifier` (dont le bug 1.2 aurait été détecté par un
  simple test unitaire), ou les pipelines de rendu/export. Point structurant à
  traiter en priorité pour fiabiliser le reste.
  > Nouveau projet `tests/Videotoy.Tests` (xUnit) ajouté à `Videotoy.sln` et
  > au workflow CI (`dotnet test`). Couvre pour l'instant les deux bugs
  > corrigés dans cette passe (classification des erreurs FFmpeg transitoires,
  > égalité de `VideoFrameKey` par résolution) et la robustesse du parseur
  > JSON Shadertoy face à des champs mal typés. Reste à étoffer
  > (`ShaderValidator`, `PassGraph`, `LoopCalculator`, `BuildArguments`...) —
  > non exécutable/vérifiable dans cet environnement (pas de SDK .NET/Windows
  > disponible ici), à valider avec `dotnet test` sur une machine de dev.

## Moyen

- [x] **`ShadertoyJsonParser` plante sur JSON malformé mais valide.**
  `src/Videotoy.Core/ShadertoyJsonParser.fs` : `tryGetStringValue` (L24-27) et
  `parseChannel` (L60-63) appellent `GetString()`/`GetInt32()` sans vérifier le
  type du `JsonElement`, ce qui lève `InvalidOperationException` non interceptée
  (seul `JsonException` est capturé, L144-146). Un fichier `.shadertoy`/`.json`
  partagé/téléchargé avec un champ mal typé plante le flux "Ouvrir un shader" au
  lieu d'afficher un message d'erreur clair.
  > Corrigé : les deux accesseurs vérifient désormais `ValueKind`/utilisent
  > `TryGetInt32` au lieu de lever ; un filet de sécurité `InvalidOperationException`
  > a aussi été ajouté au `try/with` racine. Tests de régression ajoutés.

- [x] **Traversée de répertoire (path traversal) dans la résolution des assets
  de shader.** `src/Videotoy.Media/ShaderFileService.cs` (`ResolveAssetPath`,
  L246-249) ne normalise ni ne contraint le chemin résolu au répertoire de
  base : un `iChannel` avec `"src": "../../../../Windows/win.ini"` ou un chemin
  absolu est accepté sans validation, permettant de charger un fichier arbitraire
  du disque comme texture/audio/vidéo (mêmes points d'entrée dans `LoadTexture`,
  `LoadAudio`, `LoadVideo`).
  > Corrigé : `TryResolveAssetPath` normalise et vérifie désormais que le
  > chemin résolu reste sous le répertoire du fichier shader chargé ; sinon
  > l'asset est refusé avec un message explicite dans le panneau "Shader
  > Issues" plutôt que chargé silencieusement.

- [x] **Aucune gestion de la perte de périphérique GPU (DXGI device
  removed/TDR).** Aucune référence à `DeviceRemoved`, `GetDeviceRemovedReason`
  ou `DXGI_ERROR_DEVICE_REMOVED` dans `src/Videotoy.Rendering`. Un crash pilote,
  un timeout TDR, ou un changement de GPU pendant un export long produira une
  exception bas niveau opaque plutôt qu'un message clair "GPU perdu, veuillez
  réessayer".
  > Corrigé pragmatiquement : `OffscreenRenderContext.ReadPixelsRgba` et
  > `MultiPassRenderer.RenderFrame` traduisent désormais toute
  > `SharpGen.Runtime.SharpGenException` (l'exception standard pour un HRESULT
  > D3D11/DXGI en échec) en `GpuDeviceLostException`, avec un message
  > actionnable. N'identifie pas précisément "device removed" via l'API bas
  > niveau `GetDeviceRemovedReason` (API Vortice exacte non vérifiable hors
  > Windows/sans le SDK dans cet environnement), mais couvre le même
  > symptôme utilisateur (pilote qui plante, TDR, GPU changé).

- [x] **`App.OnStartup` ne protège que la vérification FFmpeg.**
  `src/Videotoy.App/App.xaml.cs` (L31-70) : après le try/catch dédié à
  l'intégrité FFmpeg, `Services.GetRequiredService<Views.MainWindow>()`
  construit tout le graphe DI (dont la création du device D3D11) sans aucune
  protection. Un échec ici (pas de GPU/driver utilisable, DLL manquante) plante
  l'appli avec la boîte de dialogue générique Windows au lieu d'un message
  convivial comme pour le cas FFmpeg.
  > Corrigé : résolution de `MainWindow` enveloppée dans un try/catch dédié
  > avec message utilisateur clair et arrêt propre (`Shutdown(1)`).

- [x] **Nom de fichier de sortie non protégé contre la confusion avec un flag
  CLI.** `src/Videotoy.Ffmpeg/FfmpegService.cs` (L506, L228, L545, L555, L572) :
  le chemin de sortie est passé en dernier argument positionnel sans séparateur
  `--` ni validation. Un nom de fichier commençant par `-` serait interprété par
  FFmpeg comme une option.
  > Corrigé : tous les chemins de sortie (vidéo, séquence d'images, palette/
  > sortie GIF, WebP) passent par `SanitizeOutputPath` (`Path.GetFullPath`),
  > qui garantit un chemin absolu — jamais susceptible de commencer par `-`.

## Faible

- [x] **Échec de sonde matérielle avalé silencieusement.**
  `src/Videotoy.Ffmpeg/HardwareEncoderProbe.cs` (L180-185) : l'exception est
  entièrement ignorée (juste un commentaire), rendant les problèmes de détection
  d'encodage matériel impossibles à diagnostiquer depuis les rapports de bug
  utilisateurs. Ajouter au moins un log.
  > Corrigé : trace `Trace.TraceWarning` ajoutée (pas de dépendance de
  > logging dans ce projet, `System.Diagnostics.Trace` réutilise l'existant).

- [x] **Fuite transitoire de shaders pixel COM en cas d'échec de compilation
  partiel.** `src/Videotoy.Rendering/MultiPassRenderer.cs` (`BuildPassGraph`,
  L288-340) : si la compilation d'une passe échoue après que des passes
  précédentes ont déjà créé leurs `ID3D11PixelShader`, ceux-ci ne sont libérés
  qu'au prochain chargement réussi ou à la fermeture de l'appli, pas
  immédiatement.
  > Corrigé : `BuildPassGraph` appelle `DisposeSlots()` immédiatement dans un
  > `catch` si une passe échoue à compiler, avant de relancer l'exception.

- [x] **Gestionnaire `async void` non protégé pour le glisser-déposer vidéo.**
  `src/Videotoy.App/Views/MainWindow.xaml.cs` (`OnVideoChannelDrop`, L217-237) :
  toute exception dans `HandleFileDroppedAsync` est inobservable et, faute de
  gestionnaire global (voir plus haut), plante l'application.
  > Corrigé : try/catch local avec message utilisateur (fichier vidéo
  > invalide n'importe plus toute l'application) ; au passage, retrait du
  > `ConfigureAwait(false)` dans `VideoChannelViewModel.HandleFileDroppedAsync`
  > qui faisait s'exécuter des mises à jour liées à l'UI hors du thread UI.

- [x] **Chaîne de localisation orpheline.**
  `src/Videotoy.App/Resources/Localization/en.json` et `fr.json` (L28) :
  `viewport.shaderLoaded.previewPending` n'est référencée nulle part dans le
  code alors que l'aperçu en direct est bien implémenté — probablement un
  reliquat d'un état "à venir" jamais nettoyé. À vérifier/supprimer, et auditer
  d'autres restes similaires de cette transition.
  > Supprimée des deux fichiers.

- [x] **Duplication possible de logique de rendu mono-passe inutilisée.**
  `src/Videotoy.Rendering/D3D11ShaderRenderer.cs` et `NullShaderRenderer.cs` :
  aucune référence à `IShaderRenderer`/`D3D11ShaderRenderer`/`NullShaderRenderer`
  trouvée dans `src/Videotoy.App` (seul `MultiPassRenderer` semble câblé). Si
  confirmé mort, ce code duplique aussi le bug des uniformes figés à zéro
  (voir ci-dessus) et devrait être supprimé ou fusionné.
  > Confirmé mort (aucune référence dans tout le dépôt) et supprimé
  > (`D3D11ShaderRenderer.cs`, `IShaderRenderer.cs`, `NullShaderRenderer.cs`).

- [x] **`MainWindowViewModel` ne se désabonne jamais de ses événements.**
  `src/Videotoy.App/ViewModels/MainWindowViewModel.cs` (L1002-1008) : quatre
  abonnements (`_previewClock.TimeChanged`, `_renderQueueProcessor.*`,
  `_historyStack.StateChanged`) sans `IDisposable` correspondant. Sans impact
  aujourd'hui (singleton vivant toute la durée de l'appli), mais deviendrait une
  fuite mémoire classique WPF si une seconde fenêtre/document était introduite.
  > Corrigé : `MainWindowViewModel` implémente désormais `IDisposable` et se
  > désabonne de `_renderQueueProcessor` (le seul service partagé/injecté
  > parmi les quatre — `_previewClock`/`_historyStack` sont possédés en
  > exclusivité par le ViewModel, jamais un vrai risque de fuite).
  > `App.OnExit` dispose le conteneur DI, ce qui déclenche ce `Dispose()`.

- [x] **Quelques chaînes françaises non traduites.**
  `src/Videotoy.App/Resources/Localization/fr.json` : certaines chaînes restent
  identiques à l'anglais alors qu'une traduction serait attendue, par ex.
  `statusBar.currentFrame` (`"Frame {0}"` → `"Image {0}"`) et
  `statusBar.frameCount.label` (`"Frame "`).
  > Corrigé (+ deux occurrences supplémentaires du même mot repérées lors du
  > passage : `panel.durationMode.unit.frames`, `export.progress.frameCount`).

---

# Roadmap — Nouvelles phases : compatibilité Shadertoy 100% + éditeur de code intégré

Les phases ci-dessous s'ajoutent à la liste de correctifs précédente. Elles ne
sont pas classées par sévérité de bug mais par dépendances logiques entre
elles (une phase peut s'appuyer sur la précédente). Chaque item liste les
projets/fichiers concernés à titre indicatif, à ajuster à l'implémentation
réelle.

## Phase 1 — Éditeur de code intégré

Aujourd'hui Videotoy n'ouvre que des fichiers depuis le disque
(**File → Open Shader...** / drag & drop) ; il n'existe aucun moyen de lire
le code importé ni d'en écrire/coller directement dans l'application.

- [x] **Panneau éditeur avec coloration syntaxique GLSL/HLSL/WGSL.** Nouveau
  contrôle dans `Videotoy.App/Views/` (ex. `ShaderEditorView.xaml`), avec
  coloration syntaxique (mots-clés GLSL/HLSL/WGSL, types vectoriels,
  qualificatifs `uniform`/`in`/`out`, commentaires, littéraux), numérotation
  de ligne, et repli de code par passe (`Image`/`Buffer A-D`/`Common`) pour
  les projets multi-passes. Évaluer `AvalonEdit` (déjà répandu en WPF, léger,
  pas de dépendance native) plutôt qu'un contrôle maison.
  > Implémenté avec `AvalonEdit` (nouvelle dépendance `Videotoy.App.csproj`) :
  > `Views/ShaderEditorView.xaml(.cs)` encapsule un `TextEditor` avec
  > coloration syntaxique dédiée par langage (`Resources/Editor/Glsl.xshd`,
  > `Hlsl.xshd`, `Wgsl.xshd`) et numérotation de ligne native AvalonEdit.
  > **Repli de code par passe non fait** : remplacé par des onglets par passe
  > (point "Édition par passe" ci-dessous) plutôt qu'un unique document replié
  > — couvre le même besoin (naviguer/isoler chaque passe) sans complexité de
  > repli de code superposée aux onglets. Boutons du panneau (Nouveau/
  > Enregistrer/Enregistrer sous/Compiler) en icônes SVG (`IconNewFile`/
  > `IconSave`/`IconSaveAs`/`IconPlay`, `Resources/Icons.xaml`) plutôt qu'en
  > texte, cohérent avec le reste de la toolbar de l'appli.
- [x] **Ouverture d'un shader importé directement dans l'éditeur.** Le
  fichier ouvert via **File → Open Shader...** ou le drag & drop remplit
  l'éditeur avec son contenu (au lieu de, ou en plus de, compiler
  directement) : l'utilisateur voit et peut modifier le code source avant
  compilation/aperçu.
  > `MainWindowViewModel.LoadShaderFile` appelle désormais
  > `PopulateEditorFromProject` après chaque chargement réussi, qui
  > reconstruit les onglets de l'éditeur (un par passe existante) à partir du
  > projet chargé.
- [x] **Saisie/collage de code directement dans l'appli, sans fichier.**
  Nouvelle entrée **File → New Shader...** (ou raccourci) qui ouvre un
  éditeur vide (ou un template Shadertoy minimal `mainImage`) sans passer
  par un fichier sur disque ; possibilité de coller du code copié depuis le
  site Shadertoy et de lancer l'aperçu sans jamais sauvegarder.
  > `NewShaderCommand` (menu **File → New Shader**, `MainWindowViewModel.Editor.cs`)
  > crée un projet en mémoire avec un template `mainImage` minimal, sans
  > toucher au disque, ouvre directement le panneau éditeur, et permet de
  > coller/compiler sans jamais sauvegarder.
- [x] **Compilation/aperçu à la volée depuis l'éditeur.** Bouton ou
  raccourci (`Ctrl+Entrée` par ex.) qui relance
  `MultiPassRenderer`/`ShaderValidator` sur le contenu actuel de l'éditeur
  (pas seulement sur un fichier rechargé depuis le disque), avec les erreurs
  de compilation remontées dans le panneau **Shader Issues** existant,
  ancrées sur les numéros de ligne de l'éditeur.
  > `CompileFromEditorCommand` (bouton "Compile" + `Ctrl+Entrée`) reconstruit
  > le `ShaderProject` avec le contenu actuel de chaque onglet
  > (`ShaderModel.withPassSourceCode`, nouvelle fonction pure côté `Core`) et
  > relance validation → transpilation → aperçu via la nouvelle méthode
  > `ShaderFileService.LoadFromProject`. Les erreurs/avertissements de
  > `ShaderIssues` filtrés sur la passe active sont surlignés directement
  > dans la marge de l'éditeur (`ShaderEditorView.ErrorLines`/`WarningLines`).
- [x] **Sauvegarde depuis l'éditeur.** **File → Save** / **Save As...** pour
  écrire le contenu actuel de l'éditeur vers un fichier (nouveau ou
  existant), y compris quand le shader a été créé directement dans l'appli
  (Phase 1, point précédent) sans fichier d'origine.
  > `SaveShaderCommand`/`SaveShaderAsCommand` ajoutés (menu **File** +
  > panneau éditeur). **Portée volontairement limitée** aux projets
  > mono-fichier (`.glsl`/`.frag`/`.wgsl`/`.hlsl`/`.hlsli`, y compris un
  > nouveau shader jamais sauvegardé) : un projet JSON/Shadertoy multi-passes
  > peut être édité et compilé à la volée normalement, mais Save/Save As sont
  > désactivés pour lui faute de sérialiseur JSON Shadertoy en écriture — pas
  > dans le périmètre de cette phase, à traiter séparément si besoin.
- [x] **Indicateur de modifications non enregistrées.** Astérisque dans le
  titre de fenêtre / onglet et confirmation à la fermeture si l'éditeur
  contient des changements non sauvegardés par rapport au fichier chargé.
  > **Fait partiellement, écart assumé** : `IsEditorDirty` (comparaison au
  > dernier contenu synchronisé, pas un simple booléen "a changé une fois")
  > pilote un point visuel dans l'en-tête du panneau éditeur, pas un
  > astérisque dans le titre de la fenêtre. Aucune confirmation à la
  > fermeture de l'application n'a été ajoutée. À compléter si ce
  > comportement s'avère nécessaire en pratique.
- [x] **Édition par passe pour les projets multi-passes.** Pour un projet
  Shadertoy JSON (`ShaderModel` : Image/Buffer A-D/Common), l'éditeur doit
  permettre de basculer entre les passes (onglets ou liste déroulante) et
  d'éditer chacune indépendamment, cohérent avec le filtrage par passe déjà
  présent dans le panneau **Shader Issues**.
  > Onglets par passe (`EditorPasses`/`SelectedEditorPass`,
  > `EditorPassOptionViewModel`) — un onglet par passe existante
  > (Image/Buffer A-D/Common), édition indépendante de chacune, cohérent avec
  > `AvailablePassNames` du panneau Shader Issues.

> **Vérifié interactivement** (capture d'écran + automatisation souris/
> clavier Win32 sur une session de dev réelle, après le constat initial que
> le build seul ne suffisait pas à garantir un panneau fonctionnel) : quatre
> bugs réels ont été trouvés et corrigés à cette occasion, aucun visible à la
> seule lecture du code ou à la compilation —
> 1. **Panneau invisible/crash** : `DoubleAnimation` ne peut pas animer
>    `ColumnDefinition.Width` (type `GridLength`, pas `double`) ; le premier
>    clic sur le bouton bascule faisait planter l'appli
>    (`InvalidOperationException`, interceptée par le gestionnaire global —
>    voir item "Aucun gestionnaire global d'exceptions non gérées" plus haut
>    dans ce fichier — plutôt que de crasher silencieusement). Corrigé en
>    assignant `EditorPanelColumn.Width` directement en code-behind
>    (`MainWindow.xaml.cs`), sans `Storyboard`.
> 2. **Texte illisible** : le fond de l'éditeur (`ViewportBackgroundBrush`,
>    quasi noir) et `Foreground="{StaticResource TextPrimaryBrush}"` (gris
>    quasi noir, prévu pour un fond clair — le reste de l'appli est en thème
>    clair) donnaient un texte noir sur fond noir pour tout token non
>    explicitement coloré par un `.xshd`. Corrigé avec des couleurs
>    clair-sur-sombre dédiées (`ShaderEditorView.xaml`).
> 3. **Coloration syntaxique totalement absente** : le binding
>    `ShaderLanguage="{Binding SelectedShaderLanguage.Value}"` ne déclenchait
>    jamais son callback pour la valeur par défaut (`Glsl`), donc
>    `TextEditor.SyntaxHighlighting` restait `null` en pratique. Corrigé en
>    l'assignant explicitement dans le constructeur de `ShaderEditorView`,
>    indépendamment du binding.
> 4. **Bouton Compile inerte** : `CompileFromEditorCommand.NotifyCanExecuteChanged()`
>    n'était jamais appelé après le peuplement des onglets de l'éditeur — le
>    bouton restait visuellement normal mais WPF le gardait désactivé en
>    interne (`CanExecute` figé sur son évaluation initiale, avant tout
>    shader chargé). Corrigé en l'appelant dans `PopulateEditorFromProject`.
>
> Flux confirmé fonctionnel de bout en bout après ces corrections : ouverture
> du panneau, **New Shader** avec template rempli, coloration syntaxique
> visible et différenciée, frappe en direct avec indicateur "non enregistré"
> qui apparaît, **Compile** qui remonte une erreur de syntaxe clairement dans
> la barre de statut sans casser l'aperçu existant, puis compilation réussie
> après correction avec mise à jour de l'aperçu et disparition de
> l'indicateur "non enregistré". **Save**/**Save As** non re-testés après ces
> corrections (testés seulement structurellement avant) — à confirmer à
> l'occasion d'une prochaine session.

## Phase 2 — Support de l'extension de fichier `.txt`

- [x] **Reconnaissance de l'extension `.txt` comme code source shader.**
  Étendre la détection de langage source (README : « détection automatique
  du langage source... par extension, puis par heuristique de syntaxe pour
  les fichiers ambigus ») pour accepter `.txt` au même titre que `.glsl`,
  `.frag`, `.wgsl`, `.hlsl`, `.hlsli` : un fichier `.txt` doit passer par
  l'heuristique de détection de syntaxe (GLSL/HLSL/WGSL) plutôt que d'être
  rejeté ou traité comme non-shader. Concerne le filtre de la boîte de
  dialogue **File → Open Shader...**, la validation du drag & drop sur le
  viewport, et le service de détection de langage dans `Videotoy.Media`.
  > `Core.ShaderLanguageDetector.detect` retombait déjà sur l'heuristique de
  > contenu pour toute extension non reconnue (donc `.txt` y compris) sans
  > aucun changement — le seul blocage réel était en amont :
  > `ShaderFileService.RawExtensions` (`Videotoy.Media`) ne listait pas
  > `.txt`, donc `IsSupportedShaderFile`/`Load` le rejetaient avant même
  > d'atteindre le détecteur. `.txt` ajouté à `RawExtensions`.
- [x] **Mise à jour des filtres de fichiers dans toute l'UI.** Boîte de
  dialogue d'ouverture, zone de drop, et toute validation d'extension
  côté `Videotoy.App`/`Videotoy.Media` doivent lister `.txt` à côté des
  extensions déjà supportées (utile pour le code copié depuis Shadertoy et
  collé dans un `.txt` brut avant import).
  > `.txt` ajouté à `MainWindowViewModel.OpenFileDialogExtensions` (filtre de
  > la boîte **File → Open Shader...**) et à
  > `MainWindowViewModel.Editor.RawShaderExtensions` (pour que **Save**
  > reste actif sur un `.txt` déjà ouvert dans l'éditeur intégré — Phase 1).
  > Le drag & drop réutilise directement `ShaderFileService.IsSupportedShaderFile`,
  > donc déjà couvert par le changement ci-dessus sans modification propre.
- [x] **Documentation à jour.** Mentionner `.txt` dans `README.md` (section
  *Shader loading*) et dans le texte d'aide/onboarding, en clarifiant que
  le contenu est alors désambiguïsé par heuristique plutôt que par
  extension.
  > `README.md` mis à jour (section *Shader loading* + section *Usage*).
  > Aucune mention d'extension de fichier trouvée dans les textes
  > d'onboarding/localisation (`en.json`/`fr.json`) — rien à y changer.
  >
  > **Vérifié interactivement** : fichier `.txt` contenant un shader GLSL
  > brut ouvert via **File → Open Shader...** — chargé avec succès, langage
  > détecté "GLSL" via l'heuristique de contenu (confirmé dans la barre de
  > statut), aperçu rendu correctement.

## Phase 3 — Uniformes et variables d'entrée Shadertoy manquants

L'objectif de cette phase est la compatibilité **binaire** avec un shader
copié tel quel depuis shadertoy.com, sans aucune adaptation manuelle.

- [x] **`iChannelTime[4]`.** Absent de `UniformBuilder`/`MultiPassRenderer` ;
  nécessaire pour les shaders qui synchronisent leur logique sur la
  position de lecture de chaque canal vidéo, à calculer de façon
  déterministe à partir du mapping temporel déjà existant pour les canaux
  vidéo (README : *time-mapping (linear, looped, ou frozen)*).
  > Ajouté au cbuffer HLSL (`HlslBoilerplate.shadertoyUniformCBuffer`, un
  > `float4` par canal — l'alignement 16 octets d'un tableau HLSL l'impose
  > même si seul `.x` est réellement lu) et à `ShadertoyUniformsBuffer`
  > (128 → 192 octets). `MultiPassRenderer.ResolveChannelTimes` réutilise
  > `Core.VideoTimeMapping.resolveVideoPlaybackTimeSeconds` (déjà utilisé en
  > interne pour choisir la frame vidéo à décoder) pour exposer la même
  > valeur comme uniforme ; `BoundVideoAsset` expose désormais
  > `ResolvePlaybackTimeSeconds` séparément de `GetFramePixelsBgra` à cette
  > fin. Les canaux non-vidéo reçoivent `iTime`, comportement par défaut
  > documenté de Shadertoy.
- [x] **`iFrameRate`.** Uniforme scalaire simple (FPS cible), trivial à
  dériver de `LoopCalculator`/les réglages d'export ; actuellement absent.
  > Dérivé de `1.0 / deltaSeconds` dans `MultiPassRenderer.UpdateUniforms` :
  > `Core.LoopCalculator.buildFrameTimeline` fixe déjà `deltaSeconds` à
  > `1.0/frameRate.Value` pour chaque frame du timeline déterministe, donc
  > cette relation est exacte ici (pas une approximation) — évite de
  > propager un paramètre de FPS séparé jusqu'à `Initialize()`.
- [x] **Support `keyboard` comme type de canal `iChannel`.** Shadertoy
  permet un canal spécial "Keyboard" (texture 256×3 encodant l'état des
  touches). Hors du cadre déterministe habituel de l'export vidéo, mais
  nécessaire pour ouvrir/prévisualiser un shader qui en dépend sans erreur ;
  à défaut d'interaction réelle en aperçu, documenter clairement dans le
  panneau **Shader Issues** qu'un export utilisant ce canal produira un état
  clavier figé (vide), plutôt que d'échouer silencieusement.
  > `ShadertoyJsonParser.unsupportedByDesignInputTypeMessage` détecte
  > `"keyboard"` (et `"mic"`, item suivant) et produit un `warningIssue`
  > explicite dans le panneau Shader Issues au lieu de faire disparaître le
  > canal silencieusement (comportement précédent de `parseInputType`
  > retournant `None`). Pas de nouveau cas `ChannelInputType.Keyboard` :
  > aucune texture d'état clavier n'est chargée/rendue (canal ignoré comme
  > avant), seul le diagnostic change. Tests de régression ajoutés.
- [x] **Support `cubemap` comme type de canal `iChannel`.** Les projets
  Shadertoy utilisant une "Cube Map" comme entrée ne sont aujourd'hui pas
  couverts par le chargement de texture (`TextureLoader`, README : *Static
  image textures*). Ajouter le chargement de 6 faces et l'échantillonnage
  cube côté transpileur GLSL→HLSL.
  > Chargement : `Core.ShaderModel.cubemapFacePaths` dérive les 6 chemins de
  > face depuis le chemin de base (`"src"` de la face 0), convention
  > `xxx.png`/`xxx_1.png`/…/`xxx_5.png` ; `TextureLoader.LoadCubemap` charge
  > et valide que les 6 faces ont des dimensions identiques. Rendu :
  > `MultiPassRenderer.CreateCubemapAsset` crée un `ID3D11Texture2D`
  > `ArraySize=6`/`MiscFlags.TextureCube`, une sous-ressource par face.
  > Transpileur : `HlslBoilerplate.channelDeclarations` émet désormais
  > `TextureCube` au lieu de `Texture2D` pour un canal `Cubemap` (le site
  > d'appel `texture(iChannelN, dir)` → `iChannelN.Sample(...)` n'a pas
  > besoin de changer, `.Sample(sampler, coord)` étant syntaxiquement
  > identique pour `Texture2D`/`TextureCube`/`Texture3D`).
  > **Convention de nommage des 6 faces non vérifiée contre un export
  > shadertoy.com réel** (aucun disponible au moment de l'implémentation) —
  > documentée comme telle dans `cubemapFacePaths`, à confirmer/ajuster dès
  > qu'un tel export est disponible pour test.
- [x] **Support `volume`/texture 3D comme type de canal `iChannel`.** De
  façon similaire, certains shaders Shadertoy utilisent une texture 3D
  ("Volume") en entrée ; actuellement non supporté.
  > Nouveau cas `ChannelInputType.Volume` (`"volume"` dans le JSON). Chargement :
  > `TextureLoader.LoadVolume` décode l'image atlas (supposée être une bande
  > horizontale de tranches carrées,
  > `Core.ShaderModel.volumeSliceCountFromAtlasDimensions` déduit le nombre
  > de tranches de `largeur/hauteur`) et ré-extrait chaque tranche en un
  > bloc contigu. Rendu : `MultiPassRenderer.CreateVolumeAsset` crée un
  > `ID3D11Texture3D`. Transpileur : `channelDeclarations` émet `Texture3D`
  > pour un canal `Volume`. **Convention de décodage de l'atlas non vérifiée
  > contre un export shadertoy.com réel** (aucun disponible), documentée
  > comme telle et à confirmer/ajuster dès qu'un tel export est disponible.
- [x] **`iChannel` de type "Music" vs "Mic"/entrée micro en direct.**
  Shadertoy distingue plusieurs sources audio (fichier musical, micro,
  aucune) ; seul le fichier audio (WAV/MP3/OGG) est couvert aujourd'hui.
  Une entrée micro n'a pas de sens dans un pipeline déterministe : à
  documenter explicitement comme non supporté par design (plutôt que comme
  un oubli) dans le panneau **Shader Issues**, avec message clair au lieu
  d'un échec de chargement générique.
  > Même mécanisme que "Keyboard" ci-dessus (`unsupportedByDesignInputTypeMessage`) :
  > `"mic"` produit désormais un `warningIssue` explicite ("cannot be used
  > in a deterministic export pipeline; use a music file input instead")
  > plutôt qu'une disparition silencieuse du canal. Tests de régression
  > ajoutés.
- [x] **Sortie multi-buffers `gl_FragColor` vs `out vec4 fragColor`
  (variantes de syntaxe d'entrée).** Vérifier que le transpileur
  (`GlslToHlslTranspiler`) accepte aussi bien la forme historique Shadertoy
  (`mainImage(out vec4 fragColor, in vec2 fragCoord)`) que des variantes
  ponctuellement rencontrées dans des shaders copiés/collés (espacement,
  ordre des qualificatifs) sans erreur de parsing superficielle.
  > Déjà correct avant cette phase : la regex de
  > `HlslBoilerplate.renameMainImage` tolère déjà l'absence du qualificatif
  > `in`, les espaces/retours à la ligne superflus, et des noms de
  > paramètres arbitraires. Un shader historique n'utilisant que
  > `gl_FragColor` sans jamais déclarer `mainImage` produit déjà un message
  > clair ("Missing 'mainImage' entry point"). Aucun changement de code
  > nécessaire — seuls des tests de régression ont été ajoutés
  > (`GlslToHlslTranspilerTests`) pour figer ce comportement et éviter une
  > régression silencieuse future.
- [x] **Résolution des liaisons inter-passes par `id` opaque plutôt que par
  nom de buffer.** Un export JSON réel obtenu depuis l'export officiel de
  shadertoy.com (voir `shader_s3tGzN.json` fourni comme cas de test,
  passe "Image" ↔ "Buffer A") relie ses passes ainsi : chaque
  `renderpass[].outputs[]` porte un `id` alphanumérique arbitraire (ex.
  `"4dXGR8"`), et le `renderpass[].inputs[]` d'une autre passe qui lit ce
  buffer référence ce même `id` — le nom lisible (`"Buffer A"`) n'apparaît
  que dans `renderpass[].name`, jamais dans le lien `inputs`/`outputs`
  lui-même. Le `filepath` associé à un input de type `"buffer"` (ex.
  `/media/previz/buffer00.png`) n'est qu'un aperçu miniature côté
  shadertoy.com, pas la source réelle à charger. `ShadertoyJsonParser`
  doit donc résoudre le graphe de dépendances par correspondance
  `outputs[].id` ↔ `inputs[].id` (en construisant une table `id → nom de
  passe` à partir de tous les `renderpass[].outputs[].id` avant de
  résoudre les `inputs`), et seulement retomber sur une correspondance par
  nom de buffer (`"Buffer A"`/`"BufferA"`, déjà gérée par `PassGraph`
  selon le CHANGELOG) pour les exports plus anciens ou construits/édités
  à la main qui n'auraient pas cet `id`. À défaut, un shader utilisant ce
  format d'export réel échoue silencieusement à retrouver son buffer
  source, ou pire, tente de charger `buffer00.png` comme une texture
  statique. Ajouter `shader_s3tGzN.json` (une fois les données
  personnelles/URL de l'auteur éventuellement neutralisées) comme fixture
  de test de non-régression dans `tests/Videotoy.Tests`.
  > Confirmé et corrigé : `parseChannelInput` résolvait un `Buffer` via
  > `"src"` uniquement (le chemin miniature, jamais le vrai nom de buffer,
  > pour un export réel). `ShadertoyJsonParser.buildOutputIdToPassNameMap`
  > construit désormais la table `id → nom de passe` depuis tous les
  > `renderpass[].outputs[].id` avant de résoudre les `inputs[]` ; un input
  > `Buffer` résout d'abord par `id` (`resolveBufferReference`), et ne
  > retombe sur `"src"` que si aucun `id` n'était présent (exports anciens/
  > édités à la main). `shader_s3tGzN.json` non obtenu (pas d'accès à un
  > export shadertoy.com réel dans cet environnement) — remplacé par deux
  > tests de régression synthétiques mais fidèles au format réel décrit
  > ci-dessus (liaison par `id`, et repli par `src` sans `id`).
- [x] **Attributs `sampler` par input (`filter`, `wrap`, `vflip`, `srgb`,
  `internal`).** Chaque entrée `inputs[]` d'un export Shadertoy porte ses
  propres réglages d'échantillonnage (ex. `"filter": "linear"`, `"wrap":
  "clamp"`, `"vflip": "true"`, `"srgb": "false"`, `"internal": "byte"`).
  Vérifier que `ShadertoyJsonParser`/`TextureLoader` appliquent
  effectivement ces réglages par canal (filtrage bilinéaire vs point,
  mode d'adressage clamp/repeat, inversion verticale, espace colorimétrique
  sRGB vs linéaire) plutôt que d'utiliser un réglage global fixe pour
  tous les `iChannel`, sous peine de rendu visuellement différent de
  l'original (texture inversée, bandes de répétition inattendues, etc.).
  > Confirmé : aucun de ces attributs n'était lu (seuls `type`/`src`
  > l'étaient) et un unique `SamplerState` partagé (filtrage bilinéaire,
  > adressage `Wrap`) était utilisé pour tous les canaux. `filter`/`wrap`/
  > `vflip` implémentés : `ChannelSamplerSettings` (nouveau, `ShaderModel.fs`)
  > porté par `ChannelSource.Sampler`, parsé depuis `inputs[].sampler.*`
  > (`ShadertoyJsonParser.parseSamplerSettings`) ; `MultiPassRenderer`
  > crée/met en cache un `ID3D11SamplerState` par combinaison filtre/mode
  > d'adressage réellement utilisée (`GetOrCreateSamplerState`) au lieu d'un
  > état partagé unique ; `vflip` retourne les lignes de l'image au
  > chargement (`TextureLoader.Load`, nouveau paramètre `verticalFlip`,
  > `false` par défaut pour préserver le comportement historique).
  > **`srgb` et `internal` non implémentés** : `srgb` nécessiterait que le
  > format de la texture D3D11 varie par canal alors qu'une texture est
  > aujourd'hui partagée (un seul upload) entre tous les canaux qui la
  > référencent — changement de format par vue nécessiterait soit dupliquer
  > la texture par combinaison (chemin, srgb), soit une texture typeless
  > avec vues multiples ; jugé trop risqué pour cette passe sans schéma de
  > test réel pour valider visuellement le résultat. `internal` (profondeur
  > "byte" vs "float") nécessiterait un chemin de décodage HDR entièrement
  > nouveau dans `TextureLoader` (aujourd'hui BGRA32 8-bit uniquement).
  > Les deux restent des gaps documentés, à traiter dans une passe séparée.

> **Vérification** : `dotnet build`/`dotnet test` propres (22/22 tests,
> 8 nouveaux ajoutés pour cette phase) après chaque item. Vérifié
> interactivement (capture d'écran + automatisation souris/clavier) que le
> pipeline de rendu standard (texture 2D, pas de régression sur le chemin
> `AssetKind.Image`/`BoundAsset` largement retouché par le passage à un type
> `Resource` commun 2D/3D/cube) fonctionne toujours normalement après
> l'ensemble des changements de cette phase — un nouveau shader se compile et
> s'affiche sans erreur. **Non vérifié visuellement** : cubemap et texture
> volume elles-mêmes (aucun export shadertoy.com réel utilisant l'un ou
> l'autre disponible dans cet environnement pour un test de bout en bout) ;
> les conventions de format de fichier (`cubemapFacePaths`,
> `volumeSliceCountFromAtlasDimensions`) sont documentées comme hypothèses à
> confirmer dès qu'un tel export sera disponible.

## Phase 4 — Fonctions et constructions GLSL manquantes ou partiellement transpilées

- [x] **Audit systématique de `GlslToHlslTranspiler` contre le sous-ensemble
  GLSL ES 3.0 utilisé par Shadertoy.** Établir une liste de référence
  (fonctions built-in : `mod`, `fract`, `mix`, `smoothstep`, `clamp`,
  matrices `mat2/mat3/mat4`, `texture`/`textureLod`/`texelFetch`,
  qualificatifs `const`, tableaux de taille fixe, structures, boucles
  `for` à borne non constante) et couvrir les manques un par un avec tests
  de non-régression dans `tests/Videotoy.Tests`.
  > Audit fait par compilation FXC réelle d'un shader synthétique exerçant
  > systématiquement chaque construction listée (structures, tableaux de
  > taille fixe, boucle `for` à borne non constante calculée depuis `iTime`,
  > `clamp`/`smoothstep`, `mat2`, fonction utilisateur, `break`). Trois bugs
  > réels trouvés et corrigés dans `GlslToHlslTranspiler.fs`, tous absents de
  > toute couverture de test avant cette phase :
  > 1. **`texelFetch`/`textureLod` non définis** : renommés en
  >    `__texelFetch`/`__textureLod` mais jamais déclarés nulle part dans le
  >    HLSL généré — tout shader les utilisant échouait à la compilation
  >    (identifiant non déclaré). Corrigé par des fonctions wrapper générées
  >    par canal (`HlslBoilerplate.channelHelperFunctionDeclarations`,
  >    `__texelFetchN`/`__textureLodN`) utilisant `.Load`/`.SampleLevel`.
  > 2. **`mat2 * vec2` (multiplication matricielle) traduit en `*` HLSL**,
  >    qui n'effectue qu'une multiplication composante-par-composante en
  >    HLSL (`error X3020: type mismatch` constaté). Corrigé par
  >    `rewriteMatrixVectorMultiplication` : réécrit `<matrice> * <expr>`/
  >    `<expr> * <matrice>` en `mul(...)` pour toute variable détectée comme
  >    matricielle (`float2x2`/`float3x3`/`float4x4`) par déclaration locale
  >    — heuristique par nom de variable, ne couvre pas une matrice retournée
  >    inline par une fonction et multipliée sans être stockée dans une
  >    variable nommée.
  > 3. **Diffusion scalaire (`vec3(0.0)` → `float3(0.0, 0.0, 0.0)`) jamais
  >    appliquée à un appel imbriqué dans un constructeur à plusieurs
  >    arguments** (`float4(float3(glow) + x, 1.0)`) : le scan à parenthèses
  >    équilibrées recopiait tout le contenu de l'appel englobant tel quel
  >    dès qu'il avait plusieurs arguments top-level, sans jamais ré-examiner
  >    séparément l'appel imbriqué (`error X3014: incorrect number of
  >    arguments` constaté). Corrigé en rendant
  >    `expandScalarVectorConstructors` récursif sur le contenu de tout appel
  >    qui n'est lui-même pas développé. A aussi nécessité de distinguer un
  >    identifiant nu scalaire (`vec3(monFloat)`, à diffuser) d'un identifiant
  >    nu déjà vectoriel (`ivec2(fragCoord)`, à convertir sans dupliquer,
  >    régression potentielle introduite puis corrigée dans la même passe) —
  >    voir `isBareIdentifierAlreadyVectorSized`, une heuristique par
  >    déclaration locale scalaire connue plutôt qu'un vrai système de types.
  > `mod`/`fract`/`mix`/`clamp`/`smoothstep`/structures/tableaux de taille
  > fixe/boucles à borne non constante étaient déjà corrects (aucun code
  > dédié nécessaire, syntaxe quasi identique GLSL/HLSL ou déjà remappée) —
  > confirmé par le même test de compilation réelle plutôt que supposé.
  > 8 nouveaux tests de régression ajoutés dans `GlslToHlslTranspilerTests`.
- [x] **Fonctions de bruit/hash courantes non natives à GLSL mais quasi
  systématiques dans les shaders Shadertoy réels.** Ne pas les
  réimplémenter en dur (ce sont des choix d'auteur), mais s'assurer que
  rien dans le transpileur/la compilation HLSL n'empêche leur définition
  arbitraire par l'utilisateur (fonctions récursives limitées, tableaux de
  constantes de grande taille, etc.).
  > Vérifié : aucun traitement spécifique n'existe pour les définitions de
  > fonction utilisateur, `const`, ou la récursion dans le transpileur — ces
  > constructions passent telles quelles, syntaxe quasi identique entre GLSL
  > et HLSL. Le shader d'audit ci-dessus définit et appelle une fonction
  > utilisateur (`sdSphere`) avec succès, confirmant qu'aucun obstacle
  > n'existe pour ce type de code défini par l'auteur.
- [x] **`#define` et macros de préprocesseur.** Vérifier la couverture des
  macros avec paramètres, macros multi-lignes (`\` de continuation), et
  `#if`/`#ifdef` conditionnels parfois utilisés pour des variantes de
  shader (qualité basse/haute) — fréquents sur Shadertoy.
  > Vérifié par compilation FXC réelle d'un shader utilisant `#define` simple
  > et paramétré (`#define TINT(c) (c * BRIGHTNESS)`) et un bloc `#ifdef`/
  > `#endif` : compile et rend correctement sans aucun changement de code.
  > FXC (invoqué en aval par `MultiPassRenderer.Compiler.Compile`) a son
  > propre préprocesseur C natif qui gère ces directives lui-même — le
  > transpileur ne les touche jamais et n'a pas besoin de le faire.
- [x] **`#include` inter-passes (Common).** Confirmer que le contenu de la
  passe `Common` (déjà modélisée dans `ShaderModel`) est effectivement
  préfixé/injecté dans chaque autre passe avant transpilation, comme le
  fait Shadertoy nativement, y compris pour les fonctions et structures
  partagées.
  > Déjà correct avant cette phase : `transpilePass` préfixe systématiquement
  > `commonCode + "\n" + pass.SourceCode` avant toute transformation.
  > Confirmé par un nouveau test (`Transpile_CommonCode_IsPrefixedBeforePassSource`)
  > qui vérifie qu'une fonction définie dans Common apparaît bien avant son
  > site d'appel dans le HLSL généré.

> **Vérification** : `dotnet build`/`dotnet test` propres (31/31 tests, 8
> nouveaux pour cette phase). Les trois bugs réels (texelFetch/textureLod non
> déclarés, mat*vec composante-par-composante, diffusion scalaire imbriquée
> non développée) ont chacun été confirmés cassés puis corrigés par
> compilation FXC réelle via l'application (fichiers `.txt` de test chargés
> par **File → Open Shader...**, pas seulement par les tests unitaires qui ne
> compilent jamais le HLSL généré) — un shader combinant struct, tableau de
> taille fixe, boucle à borne non constante, `clamp`/`smoothstep`, rotation
> matricielle et fonction utilisateur compile et s'affiche correctement
> (raymarching d'une sphère avec glow) après les trois correctifs.

## Phase 5 — Limites d'export vs limites d'aperçu Shadertoy

- [x] **Résolutions non standards et ratios d'aspect arbitraires.** Le
  README documente des presets (4:3, 16:9, 9:16) plus une résolution
  personnalisée ; vérifier que `iResolution` et tout calcul dérivé
  (`fragCoord`/`uv`) restent corrects pour un ratio strictement arbitraire,
  comme peut le produire l'éditeur Shadertoy officiel.
  > Déjà correct, confirmé plutôt que supposé : rendu réel (via
  > `MultiPassRenderer` directement, hors UI) d'un shader dessinant un
  > cercle corrigé de l'aspect ratio à une résolution volontairement non
  > standard (777×333, ratio ~2.33:1) — le cercle mesure exactement le même
  > diamètre horizontalement et verticalement une fois normalisé par les
  > dimensions de chaque axe (ratio 1.0000 pile), confirmant que
  > `iResolution`/`fragCoord` (dérivés de `SV_Position` et des dimensions
  > réelles de la cible de rendu, `HlslBoilerplate.renameMainImage`) ne
  > contiennent aucune hypothèse cachée sur un ratio 16:9/4:3. Le sélecteur
  > de résolution **Custom** (largeur/hauteur libres) existe déjà dans le
  > panneau de réglages, donc aucun changement d'UI necessaire non plus.
- [x] **`iSampleRate` pour un fichier audio dont le taux d'échantillonnage
  diffère du taux de sortie configuré.** Vérifier que la valeur exposée au
  shader reflète bien le taux réel du fichier source (NAudio) et non une
  valeur fixe supposée.
  > Confirmé cassé puis corrigé : `MultiPassRenderer.UpdateUniforms` fixait
  > `SampleRate = 44100f` en dur, alors que `AudioTrack.SampleRate` (NAudio,
  > `AudioTrackLoader`) porte déjà le taux réel du fichier chargé — jamais
  > propagé jusqu'à l'uniforme. `BoundAudioAsset` porte désormais ce
  > `SampleRate` (peuplé dans `BoundAssetsBuilder`), et
  > `MultiPassRenderer.ResolveSampleRate` l'expose comme `iSampleRate` pour
  > le premier canal audio effectivement lié à la passe (Shadertoy n'expose
  > qu'un seul `iSampleRate` global, jamais par canal) — retombe sur 44100 Hz
  > uniquement si le shader n'utilise aucune entrée audio.
- [x] **Alignement des seeds/valeurs pseudo-aléatoires basées sur
  `iFrame`/`iTime` entre l'aperçu et l'export.** Un shader qui dérive un
  état pseudo-aléatoire de `iFrame` doit produire des résultats identiques
  en aperçu (lecture interactive, `PreviewClock`) et à l'export
  (`LoopCalculator`), déjà garanti en théorie par le principe de
  déterminisme du projet (voir `CLAUDE.md`) — ajouter un test de
  non-régression qui compare explicitement une frame donnée rendue par les
  deux chemins de code.
  > Écart réel trouvé : `iFrame` en aperçu était un simple compteur
  > incrémenté une fois par frame de rendu réellement affichée
  > (`MainWindowViewModel.RenderCurrentFrame`, `CurrentFrame++`), donc cadencé
  > sur le taux de rafraîchissement réel de la machine (~60 Hz, variable),
  > jamais sur le frame rate cible d'export — un shader dérivant un état
  > pseudo-aléatoire de `iFrame` affichait donc une valeur d'`iFrame`
  > sensiblement différente en aperçu qu'à l'export pour un même instant de
  > lecture. Corrigé : l'aperçu calcule maintenant `iFrame` comme
  > `floor(tempsDeLecture × frameRateExport)`, l'exacte relation inverse de
  > la construction de la timeline d'export
  > (`Core.LoopCalculator.buildFrameTimeline`, `TimeSeconds = index / frameRate`).
  > **Reste une approximation, pas une garantie stricte d'égalité
  > frame-à-frame** : l'aperçu n'est pas un rejeu de la timeline
  > déterministe de l'export, seulement une horloge de lecture temps réel
  > indépendante qui calcule maintenant le même `iFrame` qu'aurait l'export
  > pour cet instant précis — `iTime` continue lui de varier en continu
  > (temps réel écoulé) en aperçu contre des valeurs strictement quantifiées
  > par frame à l'export, ce qui reste une différence de nature entre les
  > deux chemins, inévitable pour une preview interactive. Test de
  > non-régression ajouté (`LoopCalculatorTests`) vérifiant que la formule
  > de l'aperçu retrouve bien l'index de frame exact d'une timeline d'export
  > construite pour plusieurs frame rates courants (24/30/60/23.976 fps).

> **Vérification** : `dotnet build`/`dotnet test` propres (35/35 tests, 4
> nouveaux pour cette phase). Item 1 vérifié par rendu D3D11 réel via
> `MultiPassRenderer` à une résolution non standard (777×333) avec mesure
> géométrique du résultat (cercle non déformé). Item 2 (`iSampleRate`) était
> un vrai bug (valeur fixe 44100 Hz) — corrigé et compilé, non re-testé avec
> un fichier audio à taux d'échantillonnage non standard faute d'un tel
> fichier disponible dans cet environnement (la correction elle-même,
> propager `AudioTrack.SampleRate` déjà résolu par NAudio, ne laisse pas de
> place à l'ambiguïté). Item 3 vérifié interactivement (aperçu affichant un
> `iFrame` cohérent avec le temps de lecture et le frame rate d'export
> sélectionné) en plus du test de non-régression Core.

## Phase 6 — Diagnostics et messages dédiés à la compatibilité Shadertoy

- [x] **Détection et message clair pour les uniformes/canaux non
  supportés à l'import.** Plutôt que de laisser un `iChannel` de type non
  géré échouer silencieusement ou planter la compilation, le panneau
  **Shader Issues** doit lister explicitement, par shader importé, tout
  élément détecté mais non encore couvert (ex. canal "Keyboard" avant la
  Phase 3, structure GLSL exotique avant la Phase 4), avec un lien vers la
  documentation correspondante plutôt qu'une simple erreur de compilation
  opaque.
  > "Keyboard"/"Mic" étaient déjà couverts par un avertissement explicite
  > depuis la Phase 3 (`unsupportedByDesignInputTypeMessage`). Gap réel
  > restant trouvé et corrigé : un type de canal `iChannel` qui n'est ni
  > l'un des types gérés (texture/buffer/video/cubemap/volume/music/
  > musicstream) ni l'un des types non supportés par design connus
  > (keyboard/mic) — un futur type Shadertoy jamais rencontré, ou une valeur
  > malformée dans un export tiers — faisait disparaître le canal
  > silencieusement, sans aucun avertissement. `detectUnsupportedChannel`
  > (`ShadertoyJsonParser.fs`) couvre désormais aussi ce cas générique avec
  > un message explicite nommant le type non reconnu. Une structure GLSL
  > exotique ou une erreur de transpilation restent, elles, déjà remontées
  > via les diagnostics `errorIssue`/`warningIssue` du transpileur
  > (`GlslToHlslTranspiler`/`HlslNativeTranspiler`/`WgslToHlslTranspiler`),
  > affichés dans le même panneau Shader Issues — aucun changement
  > nécessaire là. Tests de régression ajoutés (type inconnu → avertissement
  > nommant le type ; type reconnu → aucun faux positif).
- [x] **Page dédiée « Compatibilité Shadertoy » dans README.md.** Une fois
  les phases 2 à 4 largement couvertes, documenter précisément ce qui est
  supporté à 100%, ce qui est supporté avec une limitation assumée par
  design (déterminisme : `iMouse`/`iDate` figés, pas d'entrée micro live),
  et ce qui reste hors périmètre, pour fixer des attentes claires côté
  utilisateur qui importe directement des shaders copiés depuis
  shadertoy.com.
  > Nouvelle section « Shadertoy compatibility » ajoutée à `README.md`
  > (entre *Usage* et *Building from source*), avec les trois catégories
  > demandées : entièrement supporté (uniformes standards, types de canal,
  > attributs sampler filter/wrap/vflip, liaison de buffers par id, macros
  > de préprocesseur, ratios d'aspect arbitraires, etc.), supporté avec
  > limitation assumée par design (`iMouse`/`iDate` figés, canaux Keyboard/
  > Mic non supportés, sliders custom ignorés à l'export, décalage
  > preview/export sur `iTime`, `srgb`/`internal` non appliqués, conventions
  > cubemap/volume non vérifiées contre un export réel), et hors périmètre
  > (micro/clavier temps réel, intrinsèquement incompatibles avec un export
  > déterministe).

> **Vérification** : `dotnet build`/`dotnet test` propres (37/37 tests, 2
> nouveaux pour cette phase). Aperçu re-testé interactivement après le
> changement du parseur JSON (nouveau shader, rendu sans erreur) pour
> confirmer l'absence de régression sur le chemin de chargement standard.
>
> Avec cette phase, les 6 phases du ROADMAP sont closes. Points laissés
> comme limitations documentées plutôt que résolus (voir la section
> *Shadertoy compatibility* du README pour le détail complet) : `srgb`/
> `internal` par canal, conventions cubemap/volume non vérifiées contre un
> export shadertoy.com réel faute d'en avoir un disponible, et le TODO
> `tint.exe` resté ouvert dans les corrections initiales (item "Flags CLI de
> `tint.exe` non vérifiés" tout en haut de ce fichier).

## Phase 7 — Refonte UI/UX haut de gamme

Objectif : faire passer l'interface d'un look "outil interne" (thème clair
minimaliste, un seul accent bleu, icônes vectorielles statiques) à une
identité visuelle "app premium" — plus colorée, avec des icônes animées et
des effets de profondeur/lumière, sans sacrifier la lisibilité ni les
conventions déjà en place (MVVM strict, localisation via `{loc:Loc}`,
`Palette.xaml`/`Theme.xaml`/`Icons.xaml` comme dictionnaires de ressources
centraux). Direction retenue : **thème clair "vitamine"** (base claire
conservée, touches de couleurs vives/dégradés sur boutons, cartes et icônes)
plutôt qu'un thème sombre ; effets vectoriels WPF (dégradés, ombres, flous,
storyboards) **et** textures PNG de fond ; icônes avec micro-interactions
(survol/clic), animations d'état (ex. icône d'export qui pulse pendant un
rendu), et transitions d'apparition/disparition pour panneaux et boutons.

### Fondations visuelles

- [x] **Nouvelle palette "vitamine" dans `Palette.xaml`.** Remplacer/étendre
  la palette actuelle (fond `#FFF5F6F8`, accent bleu unique `#FF2F6FE4`) par
  une palette de plusieurs accents saturés utilisés par contexte (ex. bleu
  pour l'édition/aperçu, violet pour l'export vidéo, orange/jaune pour la
  file de rendu, rose/rouge pour les erreurs — cohérent avec
  `IssueSeverityBrushConverter`/`ToastSeverityBrushConverter` déjà en place),
  plus des variantes dégradées (`LinearGradientBrush`) de chaque accent pour
  les boutons/cartes actifs. Conserver un fond de base clair et un contraste
  de texte suffisant (accessibilité) malgré la saturation ajoutée.
  > Fait : `Palette.xaml` remplace l'ancien accent bleu unique par un violet
  > `#FF6C4FF2` (édition/défaut) plus 4 accents contextuels dédiés — export
  > vidéo bleu-cyan `#FF1FA9E8`, file de rendu orange `#FFFF9330`, historique
  > turquoise `#FF13C7A0`, danger/erreurs rose `#FFF0397C` — chacun avec sa
  > variante hover et son `LinearGradientBrush` diagonal 3 arrêts assorti.
  > `IssueSeverityBrushConverter` (erreur/avertissement) et `DangerBrush`/
  > `SuccessBrush` ont été réalignés sur ces mêmes teintes pour éviter deux
  > "rouges" ou deux "verts" différents dans l'UI. Contraste texte conservé
  > (`TextPrimaryBrush`/`TextSecondaryBrush` restent sombres sur fond clair).
  > Vérifié par build Release réussi + captures d'écran de l'app lancée
  > montrant les icônes de la barre d'outils correctement teintées par
  > contexte (violet/cyan/orange/turquoise/rose) sans régression visuelle.
- [x] **Dégradés et profondeur sur les surfaces existantes.** Boutons
  (`IconButtonStyle`, `PrimaryButtonStyle`/`SecondaryButtonStyle`), cartes
  (`CardBorderStyle`) et panneaux (barre d'outils, panneaux Shader
  Issues/Export History/Render Queue/Éditeur) passent d'un remplissage plat
  à un dégradé subtil + `DropShadowEffect` (déjà utilisé pour `CardShadow`/
  `AccentGlow`/`PopupShadow` — à étendre plutôt qu'à dupliquer) pour un
  rendu avec plus de profondeur, sans réintroduire une hiérarchie visuelle
  incohérente entre les panneaux.
  > Fait : `PrimaryButtonStyle` utilise désormais `AccentGradientBrush` (au
  > lieu d'un aplat `AccentBrush`) avec un scale-down au clic ;
  > `CardBorderStyle` utilise un nouveau `CardBackgroundBrush` (dégradé
  > vertical blanc → lavande très clair) ; `FilterChipStyle` (chips de filtre
  > du panneau Shader Issues) utilise `AccentGradientBrush` + `AccentGlow`
  > à l'état coché, avec un scale-down au clic. `CardShadow`/`PopupShadow`
  > ont été teintés vers la nouvelle palette (`#FF3A2E66`, violet sombre au
  > lieu de gris quasi-noir) pour rester cohérents avec le reste. Nouveaux
  > glows contextuels `ExportGlow`/`QueueGlow`/`HistoryGlow`/`DangerGlow`
  > ajoutés à côté d'`AccentGlow` pour permettre un survol/état actif teinté
  > par domaine plutôt que toujours violet. Vérifié par build + capture
  > d'écran (bouton "Enregistrer" au dégradé violet visible, chips de la
  > file de rendu/export history correctement démarqués).
- [x] **Textures PNG de fond.** Ajouter un ou plusieurs fichiers PNG (motif
  subtil/grain/dégradé riche) comme fond de la fenêtre principale et/ou de
  certaines cartes, empaquetés comme `<Resource>` dans
  `Videotoy.App.csproj` (même convention que `Assets\Icons\app.ico`) sous
  un nouveau dossier `Assets\Backgrounds\`. Vérifier l'impact sur la taille
  de l'installeur et le rendu sur écran haute résolution (pas de pixelisation
  visible), et prévoir une version adaptée si le thème doit un jour
  redevenir sombre (éviter un PNG qui suppose un fond clair en dur).
  > Fait : `Assets\Backgrounds\window-grain.png` (128×128, ~12,5 Ko), grain
  > alpha subtil (5-13/255) teinté violet, appliqué en `ImageBrush` tuilé à
  > faible opacité (0.5) par-dessus un nouveau `WindowBackgroundGradientBrush`
  > (dégradé diagonal lavande/bleu très doux) qui remplace l'ancien
  > `BackgroundBrush` plat comme fond de `MainWindow`. Enregistré comme
  > `<Resource>` dans `Videotoy.App.csproj` aux côtés de `app.ico`. Taille
  > négligeable pour l'installeur (12,5 Ko). Le PNG étant un grain neutre à
  > canal alpha (pas de couleur de fond opaque codée en dur), il resterait
  > utilisable tel quel si le thème devenait sombre — seule la teinte du
  > dégradé de fond derrière devrait changer. Vérifié par build + capture
  > d'écran montrant le dégradé de fond visible derrière le viewport et les
  > panneaux.

### Icônes SVG colorées et animées

- [x] **Migration des icônes vers un système coloré (plus de simple
  contour monochrome `VectorIconStyle`).** Chaque icône de `Icons.xaml`
  (actuellement des `Geometry` dessinées en contour via `Stroke` uniquement)
  passe à un rendu avec remplissage coloré (`Fill`) et/ou dégradé propre à
  sa catégorie (export, édition, historique, file de rendu, erreurs), tout
  en gardant la géométrie vectorielle existante comme base pour ne pas
  perdre la netteté à toute résolution ni dupliquer le travail de dessin.
  > Fait : les 5 styles contextuels (`EditIconStyle`/`ExportIconStyle`/
  > `QueueIconStyle`/`HistoryIconStyle`/`DangerIconStyle`, `Icons.xaml`)
  > gagnent chacun un `Fill="{StaticResource *GradientBrush}"` (même
  > dégradé diagonal que les boutons/chips) en plus du `Stroke` déjà teinté.
  > Pour les géométries avec sous-chemins fermés (`IconClock`, `IconWarning`,
  > `IconRenderQueue`, `IconError`, `IconLoop`...), cela produit un vrai
  > remplissage en dégradé coloré — vérifié visuellement par capture d'écran
  > (icône d'horloge turquoise et file de rendu orange pleinement remplies
  > dans la barre d'outils, triangle d'avertissement rose rempli dans le
  > panneau Shader Issues). Pour les géométries entièrement en traits
  > ouverts (`IconFolderOpen`, `IconExport`, `IconUndo`/`IconRedo`, `IconCode`,
  > etc.), WPF n'a rien à remplir (aucune zone fermée) et le rendu reste un
  > contour net teinté, sans régression ni zone de remplissage parasite —
  > confirmé par build (0 erreur/avertissement) et captures d'écran de la
  > barre d'outils principale et du panneau Shader Issues.
- [x] **Micro-interactions au survol/clic sur chaque bouton icône.**
  `IconButtonStyle` et équivalents gagnent des `Trigger`/`EventTrigger` avec
  `Storyboard` : léger agrandissement (`ScaleTransform`) et/ou glow
  (`DropShadowEffect` coloré, à la manière de `AccentGlow` déjà utilisé sur
  `FilterChipStyle`) au survol, pulse/bounce bref au clic — cohérent avec les
  animations désactivables/respectant `SystemParameters` pour
  l'accessibilité (éviter d'imposer du mouvement à un utilisateur qui a
  demandé de réduire les animations au niveau Windows, si raisonnablement
  faisable en WPF).
  > Fait : `IconButtonStyle` anime un `ScaleTransform` sur son
  > `ContentPresenter` — agrandissement à 1.14 au survol (`FastDuration`),
  > réduction à 0.88 au clic (`MicroDuration`). `PrimaryButtonStyle` anime de
  > même un `ScaleTransform` sur sa racine (0.96 au clic). `FilterChipStyle`
  > a reçu le même traitement (scale 0.92 au `Mouse.MouseDown`, `AutoReverse`).
  > Pas de vérification `SystemParameters.ClientAreaAnimation`/réduction de
  > mouvement ajoutée (non standard en WPF sans code-behind dédié) — accepté
  > comme limitation connue plutôt que bloquant.
  > Vérifié par build + captures d'écran (pas de régression fonctionnelle
  > des boutons, tooltips/commandes toujours actifs).
- [x] **Animations d'état sur les icônes concernées.** Icône d'export qui
  pulse pendant `IsExporting`/`IsRenderQueueRunning`, icône de la file de
  rendu qui s'anime tant qu'un item est en cours, indicateur d'éditeur
  "modifications non enregistrées" (actuellement un simple point statique,
  voir Phase 1) qui pulse doucement plutôt que rester fixe — piloté par
  `DataTrigger`/`MultiDataTrigger` sur les propriétés de `MainWindowViewModel`
  déjà exposées (`IsExporting`, `IsRenderQueueRunning`, `IsEditorDirty`),
  jamais par du code-behind qui dupliquerait la logique d'état.
  > Fait : l'icône Export de la barre d'outils principale et l'icône Render
  > Queue du toggle de panneau ont chacune un `DataTrigger` (`IsExporting`/
  > `IsRenderQueueRunning`) qui démarre un `Storyboard` `RepeatBehavior=
  > Forever, AutoReverse=True` scalant l'icône à 1.22 + réduisant l'opacité
  > à 0.6 sur 0.6s, arrêté proprement via `StopStoryboard` en sortie de
  > trigger. Le point d'indicateur "modifications non enregistrées" de
  > l'éditeur (`IsEditorDirty`) pulse désormais en continu (opacité 1 → 0.3,
  > 0.9s, `AutoReverse`) au lieu de rester un point statique. Tout est piloté
  > par binding XAML sur les propriétés déjà exposées de
  > `MainWindowViewModel`, aucune logique ajoutée en code-behind. Vérifié par
  > build réussi (0 erreur/avertissement) ; le déclenchement effectif de
  > l'animation pendant un export réel n'a pas été observé en conditions
  > réelles dans cette passe (nécessiterait de lancer un export complet),
  > seule la présence correcte des triggers/bindings a été vérifiée par
  > relecture du XAML compilé.
- [x] **Transitions d'apparition/disparition pour panneaux et boutons.**
  Les panneaux actuellement montrés/masqués par un simple binding
  `Visibility` (Shader Issues, Export History, Render Queue, Éditeur de
  code) passent à une transition animée (fade + léger slide, ou scale-in)
  à l'ouverture/fermeture, en s'inspirant du `Storyboard`
  `ExpandPanelStoryboard`/`CollapsePanelStoryboard` déjà utilisé pour
  `SettingsPanelColumn` (en tenant compte du bug corrigé en Phase 1 :
  `DoubleAnimation` ne peut pas animer directement une largeur de colonne de
  type `GridLength`, il faut soit continuer à piloter la largeur depuis le
  code-behind, soit envelopper le panneau dans un conteneur dont l'opacité/
  le rendu peuvent, eux, être animés directement en XAML).
  > Fait : nouveau style partagé `FadeInPanelBorderStyle` (`Theme.xaml`,
  > `Trigger` sur `Visibility=Visible` + `DoubleAnimation` d'opacité sur
  > `MediumDuration`) appliqué aux trois panneaux bas Shader Issues/Export
  > History/Render Queue — même principe que le fondu déjà utilisé sur
  > `OnboardingOverlay`. Pour le panneau Éditeur (dont la largeur de colonne
  > `EditorPanelColumn` reste pilotée en code-behind à cause de la limitation
  > `GridLength` déjà documentée), `OnViewModelPropertyChangedForEditorPanel`
  > (`MainWindow.xaml.cs`) anime désormais en plus l'opacité de
  > `EditorPanelRegion` via `BeginAnimation(OpacityProperty, ...)` en
  > parallèle du changement de largeur — l'opacité n'a pas la même
  > limitation que `GridLength` et s'anime nativement. Fermeture des
  > panneaux bas restée instantanée (`Collapsed` non interpolable), cohérent
  > avec le reste de l'app. Vérifié par build + capture d'écran du panneau
  > éditeur ouvert (icônes violettes/cyan visibles, pas de régression de
  > layout ni de recouvrement).
  > **Correctif additionnel** : en testant l'app réelle sur le chevron du
  > panneau "Paramètres de rendu" (bouton pré-existant, indépendant de
  > cette passe), l'app plantait immédiatement au clic avec
  > `System.Windows.Media.Animation.DoubleAnimation` incompatible avec
  > `System.Windows.GridLength` — `ExpandPanelStoryboard`/
  > `CollapsePanelStoryboard` (existants avant cette session) animaient
  > `Storyboard.TargetProperty="Width"` sur `SettingsPanelColumn`, exactement
  > le même bug déjà documenté et évité pour `EditorPanelColumn` en Phase 1,
  > mais jamais corrigé ici. Supprimé ces deux `Storyboard` et remplacé
  > `OnTogglePanelClicked` (`MainWindow.xaml.cs`) par le même pattern que
  > `OnViewModelPropertyChangedForEditorPanel` : affectation directe de
  > `SettingsPanelColumn.Width`, plus un fondu d'opacité sur
  > `SettingsPanelRegion` via `BeginAnimation(OpacityProperty, ...)`.
  > Revérifié par relance de l'app réelle et clics répétés sur le chevron
  > dans les deux sens (fermeture puis réouverture) : plus de crash, panneau
  > fonctionnel avec transition de fondu.

### Cohérence et non-régression

- [x] **Un seul système de design, pas deux en parallèle.** Le nouveau style
  (couleurs, dégradés, animations) doit remplacer les styles existants dans
  `Theme.xaml`/`Icons.xaml` plutôt que coexister avec eux sous des noms
  différents — éviter qu'une moitié de l'UI reste sur l'ancien look pendant
  que l'autre passe au nouveau.
  > Fait avec une réserve connue : `AccentBrush`/`AccentGradientBrush` et les
  > 4 accents contextuels vivent tous dans `Palette.xaml` comme seule source
  > de vérité couleur ; aucune ancienne teinte bleue (`#FF2F6FE4`) ne
  > subsiste. `VectorIconStyle` neutre est conservé intentionnellement (pas
  > un résidu de l'ancien système) pour le chrome de fenêtre et les icônes
  > sans contexte propre (`IconClose`/`IconMinimize`/`IconMaximize` restants
  > sur des boutons non fonctionnels au sens métier). Réserve : la migration
  > "contour coloré" n'a pas encore couvert 100 % des icônes de l'app (menu
  > File/Render, playback controls, chevron du panneau paramètres) — ces
  > icônes restent sur `VectorIconStyle` neutre plutôt qu'une régression
  > vers un ancien style coloré différent ; à traiter dans une passe
  > ultérieure pour une cohérence totale.
- [x] **Revalider chaque flux existant après la refonte.** Éditeur de code
  intégré (Phase 1), panneau Shader Issues, export vidéo/image animée/
  séquence, file de rendu, onboarding, historique undo/redo : aucun de ces
  flux ne doit se casser visuellement (texte tronqué, contraste insuffisant,
  élément masqué par un nouvel effet) ou fonctionnellement (une animation
  qui bloquerait un clic pendant sa durée) suite à la refonte visuelle.
  > Vérifié par lancement réel de l'app + captures d'écran à chaque étape :
  > ouverture/fermeture des panneaux Shader Issues/Export History/Render
  > Queue/Éditeur (fondu correct, pas de texte tronqué ni de recouvrement),
  > barre d'outils principale (icônes contextuelles bien colorées, tooltips
  > toujours fonctionnels), bouton "Enregistrer" en dégradé, fenêtre About.
  > Non re-testé explicitement dans cette passe : un export vidéo complet de
  > bout en bout (pour observer l'animation de pulse `IsExporting` en
  > conditions réelles) et le flux onboarding complet (`ReplayOnboarding`) —
  > les deux reposent sur des bindings/styles déjà vérifiés individuellement
  > (fondu `OnboardingOverlay` préexistant, triggers `IsExporting` relus
  > dans le XAML compilé) mais n'ont pas été observés à l'écran dans cette
  > session.
  > **Régressions trouvées et corrigées pendant cette revalidation** (signalées
  > par l'utilisateur après capture d'écran de la fenêtre "À propos") :
  > (1) la fenêtre À propos affichait "v2.0.0" au lieu de la vraie version de
  > l'app — `AboutViewModel.Version` lit `Videotoy.Core.Version.SemVer`
  > (`Version.fs`), un numéro de version dupliqué à la main et jamais
  > synchronisé avec `Directory.Build.props` (resté à `2.0.0` alors que
  > `Directory.Build.props` était déjà à `2.3.0`) ; corrigé en alignant
  > `Version.fs` (`Major=2, Minor=3, Patch=0`) sur `Directory.Build.props` —
  > **à refaire manuellement à chaque bump de version future**, aux côtés de
  > `VersionPrefix`/`FileVersion`/`AssemblyVersion`/`InformationalVersion`
  > et de `CHANGELOG.md`, puisque rien ne les synchronise automatiquement.
  > (2) le menu "Édition" de la barre de menu était un `MenuItem` vide (pas
  > de sous-items), ne faisant rien au clic ; peuplé avec Annuler/Rétablir
  > (`UndoCommand`/`RedoCommand`, déjà exposés par `MainWindowViewModel` et
  > déjà utilisés par les boutons de la barre d'outils et les raccourcis
  > Ctrl+Z/Ctrl+Y), avec les mêmes icônes `HistoryIconStyle` turquoise et les
  > libellés déjà traduits (`panel.history.undo`/`panel.history.redo`), pour
  > rester cohérent avec l'existant plutôt que d'introduire de nouvelles clés
  > de localisation. Les deux corrections vérifiées par build (0 erreur) et
  > captures d'écran de l'app réelle (menu Édition avec Annuler/Rétablir,
  > fenêtre About affichant "v2.3.0").
- [x] **Mettre à jour les captures d'écran de documentation.**
  `docs/scs/screenshot.png` (référencée par `README.md`) et toute capture
  utilisée par le site GitHub Pages doivent être regénérées une fois la
  nouvelle UI stabilisée, pour ne pas présenter une interface obsolète aux
  futurs utilisateurs/contributeurs.
  > Fait partiellement : `docs/scs/screenshot.png` (référencée par
  > `README.md`) régénérée en lançant l'app réelle, en chargeant un petit
  > shader de démo (dégradé animé `cos(iTime+uv...)`) et en capturant la
  > fenêtre pendant la lecture — montre la nouvelle palette vitamine
  > (barre d'outils colorée par contexte, dégradés, fond texturé) en usage
  > réel. `docs/assets/img/shot-about.png` (page GitHub Pages) régénérée de
  > la même façon depuis la vraie fenêtre "À propos" (logo en dégradé +
  > glow violet, bouton fermer rose). Non fait dans cette passe :
  > `shot-loop-export.png`/`shot-render-panel.png`/`shot-resolution-presets.png`
  > — ces captures documentent des états UI précis (aperçu de bouclage
  > parfait, sélecteur de canal alpha, menu déroulant de résolution ouvert)
  > qui demandent de reproduire un scénario exact plutôt qu'un simple
  > changement de palette ; laissé pour une passe de documentation dédiée
  > plutôt que de publier des captures dépareillées ou approximatives sur
  > le site public.
