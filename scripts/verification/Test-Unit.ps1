[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'TestResults.ps1')

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$solutionFilter = Join-Path $repo 'OpenRCT3.tests.slnf'
$settings = Join-Path $PSScriptRoot 'verification.runsettings'
$requiredUnitProjects = @(
  'OpenCobra\Tests\Tests.csproj',
  'OpenRCT3.Tests\OpenRCT3.Tests.csproj',
  'Dumper\Dumper.Tests\Dumper.Tests.csproj'
)
$solutionUnitProjects = @($requiredUnitProjects[0], $requiredUnitProjects[1])
$dumperProject = $requiredUnitProjects[2]
$approvedDumperSkip = 'Dumper.Tests.TruncatedLabelTests.TestVeryLongPath_PreservesFilename'
$approvedInstalledAssetSkips = @(
  'OpenCobra.Tests.OVL.BoneAnimationsTests.Extract_InstalledVintageCar_DecodesDeclaredBanResources',
  'OpenCobra.Tests.OVL.BoneShapesTests.Extract_InstalledSaloonBrawlPreservesUnsignedHighBoneIndices',
  'OpenCobra.Tests.OVL.ModelsTests.Extract_FromInstalledElephantsReadsStaticGeometry',
  'OpenCobra.Tests.OVL.PathTypesTests.Extract_InstalledAsphalt_PreservesDeclaredResources',
  'OpenCobra.Tests.OVL.QueueTypesTests.Extract_InstalledQueueSet1_PreservesDeclaredResources',
  'OpenCobra.Tests.OVL.RideCarsTests.Extract_FromInstalledElephantAllowsAnimalSpeciesBody',
  'OpenCobra.Tests.OVL.RideCarsTests.Extract_FromInstalledWoodenCoasterArchiveDecodesRideCars',
  'OpenCobra.Tests.OVL.RideResourceGraphTests.Resolve_InstalledLogFlumeLinksCrocLogThroughRealDependencyClosures',
  'OpenCobra.Tests.OVL.RideTrainsTests.Extract_FromInstalledWoodenCoasterArchiveDecodesTrainComposition',
  'OpenCobra.Tests.OVL.SplinesTests.Extract_FromInstalledCoasterArchiveDecodesExactSplines("Track16")',
  'OpenCobra.Tests.OVL.SplinesTests.Extract_FromInstalledCoasterArchiveDecodesExactSplines("Track59")',
  'OpenCobra.Tests.OVL.TrackSectionResourceGraphTests.Resolve_InstalledTrackArchiveLinksEverySidAndSpline("Track16",Vanilla,15)',
  'OpenCobra.Tests.OVL.TrackSectionResourceGraphTests.Resolve_InstalledTrackArchiveLinksEverySidAndSpline("Track59",Wild,145)',
  'OpenCobra.Tests.OVL.TrackSectionsTests.Extract_FromInstalledCoasterArchiveDecodesTrackSections("Track16",Vanilla)',
  'OpenCobra.Tests.OVL.TrackSectionsTests.Extract_FromInstalledCoasterArchiveDecodesTrackSections("Track59",Wild)',
  'OpenCobra.Tests.OVL.TrackSectionsTests.Extract_FromInstalledTrackBased10AcceptsMismatchedEndpointFlags',
  'OpenCobra.Tests.OVL.TrackedRideTrackResourceGraphTests.Resolve_InstalledRideLinksExternalTrackSectionsAndLocalSplines("LogFlume","TrackBased10",33,39)',
  'OpenCobra.Tests.OVL.TrackedRideTrackResourceGraphTests.Resolve_InstalledRideLinksExternalTrackSectionsAndLocalSplines("Mono","TrackBased07",15,15)',
  'OpenCobra.Tests.OVL.TrackedRideTrackResourceGraphTests.Resolve_InstalledWoodenWildMinePreservesNineExactChainTargets',
  'OpenCobra.Tests.OVL.TrackedRidesTests.Extract_FromInstalledLogFlumeArchiveDecodesTrackedRideCore',
  'OpenCobra.Tests.OVL.WildAnimalAnimationDataTests.Extract_FromInstalledElephantProvesExactLayout',
  'OpenCobra.Tests.OVL.WildAnimalAnimationDataTests.Extract_FromInstalledOstrichProvesExactLayout',
  'OpenCobra.Tests.OVL.WildAnimalSpeciesTests.Extract_FromInstalledElephantReadsExactFourVariantLayout',
  'OpenCobra.Tests.OVL.WildAnimalSpeciesTests.Extract_FromInstalledOstrichReadsExactCompactFourVariantLayout',
  'OpenRCT3.Tests.Serialization.DatRideCarInstanceReaderTests.Read_BoxOfficeCapturesExactStreamlinedMonoConsistAndResumeState',
  'OpenRCT3.Tests.Serialization.DatRideCarInstanceReaderTests.Read_ScrubGardensCapturesWildTrainVisualVariantSelections',
  'OpenRCT3.Tests.Serialization.DatTrackReaderTests.Read_BoxOfficeCapturesExactTrackPieceStartDistances',
  'OpenRCT3.Tests.Serialization.DatTrackReaderTests.Read_InstalledCampaignsAcrossExpansionsFullyParse',
  'OpenRCT3.Tests.Serialization.DatTrackedRideInstanceReaderTests.Read_BoxOfficeCapturesExactSavedRideTrainProvenance',
  'OpenRCT3.Tests.Serialization.DatTrackedRideInstanceReaderTests.Read_InstalledCampaignCapturesRideInstanceLinkage("Campaigns/Base/BoxOffice.dat")',
  'OpenRCT3.Tests.Serialization.DatTrackedRideInstanceReaderTests.Read_InstalledCampaignCapturesRideInstanceLinkage("Campaigns/Base/Soaked/Atlantis.dat")',
  'OpenRCT3.Tests.Serialization.DatTrackedRideInstanceReaderTests.Read_InstalledCampaignCapturesRideInstanceLinkage("Campaigns/Base/Wild/GeminiBasin.dat")',
  'OpenRCT3.Tests.Simulation.PathSurfaceResourceResolverTests.LoadInstalled_ResolvesOwnerThroughSameNameSvdFirstSerializedShsLod',
  'OpenRCT3.Tests.Simulation.PathSurfaceResourceResolverTests.LoadInstalled_ResolvesStockOrdinaryAndRecolouredQueueTextures',
  'OpenRCT3.Tests.Simulation.PathSurfaceResourceResolverTests.TryResolve_InstalledAsphaltAndQueueSet1MatchDatSystemNames',
  'OpenRCT3.Tests.Simulation.RideCarGeometryAdapterTests.BoxOffice_StreamlinedMonoBodiesExposeExactModelSpaceGeometry',
  'OpenRCT3.Tests.Simulation.RideCarSavedWheelCursorRegistryTests.Build_BoxOfficeResolvesAllSavedWheelContactsToExactCachedPieces',
  'OpenRCT3.Tests.Simulation.RideCarStaticInstanceRegistryTests.BoxOffice_ComposesSevenExactStaticCarsInSavedConsistOrder',
  'OpenRCT3.Tests.Simulation.RideCarVisualResourceBridgeTests.BoxOffice_ExactRideClosureResolvesCarVisualShapeResources',
  'OpenRCT3.Tests.Simulation.RideCarVisualTemplateRegistryTests.BoxOffice_BodyTemplatesHaveDeterministicGeometryCounts',
  'OpenRCT3.Tests.Simulation.RideInstanceResourceResolverTests.Resolve_InstalledCampaignIdentitiesAgainstTheirExactOvlOverlays',
  'OpenRCT3.Tests.Simulation.RideInstanceTrackGraphTests.Build_InstalledCampaignsHaveExactNonSentinelReciprocalLinks',
  'OpenRCT3.Tests.Simulation.RideTrackGunslingerInstalledPipelineTests.Gunslinger_ResolvesExactWoodenWildMineChainConstructionIdentity',
  'OpenRCT3.Tests.Simulation.RideTrackInstalledPipelineTests.BoxOffice_DatThroughCatalogProducesTypedGeometryOutcomes',
  'OpenRCT3.Tests.Simulation.RideTrackWildInstalledPipelineTests.RaidersOfTheLostCoaster_ResolvesReciprocalPiecewiseCircuitsForMotion',
  'OpenRCT3.Tests.Simulation.RideTrackWildInstalledPipelineTests.ScrubGardens_SeizmicResolvesTwoExactCircuitsAndSavedCars',
  'OpenRCT3.Tests.Simulation.RideTrackSectionResourceResolverTests.Resolve_InstalledCampaignsLinkExactDatOverlayAndTksIdentities',
  'OpenRCT3.Tests.Simulation.ScenerySceneLoaderTests.Load_ScrubGardensResolvesColonialWallVisualFromExactOverlay',
  'OpenRCT3.Tests.Simulation.SceneryVisualResolverTests.Resolve_ScrubGardensRideTrackSidStopsBeforeAmbiguousGlobalVisuals',
  'OpenRCT3.Tests.Simulation.SceneryVisualResolverTests.Resolve_ScrubGardensColonialWallUsesExactOwnerOverlay',
  'OpenRCT3.Tests.Serialization.DatWildAnimalReaderTests.Read_OstrichFarmCapturesExactWildAnimalIdentityAndFiniteMatrices',
  'OpenRCT3.Tests.Simulation.WildAnimalOstrichInstalledPipelineTests.OstrichFarm_DatThroughInstalledWasModelsAndStaticScenePreservesAllSixteenAnimals',
  'OpenRCT3.Tests.Simulation.WildAnimalOstrichInstalledPipelineTests.OstrichFarm_ProductionSceneLoaderBuildsExactTexturedSceneAndTransfersLeases',
  'OpenRCT3.Tests.Simulation.WildAnimalSpeciesModelResourceBridgeTests.ResolveInstalled_FromElephantLinksExactFourModelsInSerializedOrder',
  'OpenRCT3.Tests.Simulation.WildAnimalSpeciesModelResourceBridgeTests.ResolveInstalled_FromOstrichLinksExactFourModelsInSerializedOrder'
)
$approvedSkippedTests = @($approvedDumperSkip) + $approvedInstalledAssetSkips
$results = Join-Path $repo 'TestResults\unit'
$solutionDirectory = $repo.Replace('\', '/') + '/'

if (Test-Path -LiteralPath $results) {
  Remove-Item -LiteralPath $results -Recurse -Force
}
New-Item -ItemType Directory -Path $results -Force | Out-Null

$filter = Get-Content -Raw -LiteralPath $solutionFilter | ConvertFrom-Json
$requiredTestAssemblies = @()
foreach ($project in $requiredUnitProjects) {
  $projectPath = Join-Path $repo $project
  $isTestProject = (& dotnet msbuild $projectPath -nologo -getProperty:IsTestProject | Out-String).Trim()
  if ($LASTEXITCODE -ne 0) {
    throw "MSBuild could not inspect IsTestProject for $project."
  }
  if ($isTestProject -ne 'true') {
    throw "$project is a required unit project, but MSBuild does not mark it as a test project."
  }
  $targetPath = (& dotnet msbuild $projectPath -nologo -getProperty:TargetPath | Out-String).Trim()
  if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($targetPath)) {
    throw "MSBuild could not resolve TargetPath for required unit project $project."
  }
  $requiredTestAssemblies += [System.IO.Path]::GetFullPath($targetPath)
}
foreach ($project in $solutionUnitProjects) {
  if ($filter.solution.projects -notcontains $project) {
    throw "The unit solution filter is missing required test project $project."
  }
}
$dumperProjectPath = Join-Path $repo $dumperProject

Push-Location $repo
try {
  & (Join-Path $PSScriptRoot 'Test-Harness.ps1')

  & deno check clients/desktop/main.ts
  if ($LASTEXITCODE -ne 0) { throw "Deno check failed with exit code $LASTEXITCODE." }

  $testArgs = @(
    'test',
    $solutionFilter,
    '--no-build',
    '--no-restore',
    '--settings',
    $settings,
    '--logger',
    'trx;LogFilePrefix=unit',
    '--results-directory',
    $results,
    '--verbosity',
    'normal',
    "-p:SolutionDir=$solutionDirectory"
  )
  if ($env:COLLECT_COVERAGE -eq '1') {
    $testArgs += '--collect:XPlat Code Coverage'
  }

  & dotnet @testArgs
  if ($LASTEXITCODE -ne 0) { throw "Unit tests failed with exit code $LASTEXITCODE." }

  if ($filter.solution.projects -notcontains $dumperProject) {
    $dumperArgs = @(
      'test',
      $dumperProjectPath,
      '--no-build',
      '--no-restore',
      '--settings',
      $settings,
      '--logger',
      'trx;LogFilePrefix=unit-dumper',
      '--results-directory',
      $results,
      '--verbosity',
      'normal',
      '-p:TestingPlatformDotnetTestSupport=false',
      "-p:SolutionDir=$solutionDirectory"
    )
    if ($env:COLLECT_COVERAGE -eq '1') {
      $dumperArgs += '--collect:XPlat Code Coverage'
    }

    & dotnet @dumperArgs
    if ($LASTEXITCODE -ne 0) { throw "Dumper unit tests failed with exit code $LASTEXITCODE." }
  }
} finally {
  Pop-Location
}

$summary = Get-TrxSummary `
  -ResultsDirectory $results `
  -MinimumRuns $requiredUnitProjects.Count `
  -ApprovedSkippedTests $approvedSkippedTests `
  -ExpectedTestAssemblies $requiredTestAssemblies
Write-TestSummary -Name 'Unit' -Summary $summary
