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
