import { useEffect, useRef, useState } from 'react'
import * as THREE from 'three'
import { OrbitControls } from 'three/addons/controls/OrbitControls.js'
import {
  ArrowLeft,
  Box,
  CircleDot,
  Eye,
  EyeOff,
  Focus,
  Rotate3D,
  Truck,
} from 'lucide-react'
import './WheelLoaderViewer.css'

type ViewPreset = 'isometric' | 'side' | 'front'
type LayerName = 'wheels' | 'cab' | 'loader'

interface ModelLayers {
  wheels: THREE.Group
  cab: THREE.Group
  loader: THREE.Group
}

function addBox(
  parent: THREE.Object3D,
  size: [number, number, number],
  position: [number, number, number],
  material: THREE.Material,
  rotation: [number, number, number] = [0, 0, 0],
) {
  const mesh = new THREE.Mesh(new THREE.BoxGeometry(...size), material)
  mesh.position.set(...position)
  mesh.rotation.set(...rotation)
  mesh.castShadow = true
  mesh.receiveShadow = true
  parent.add(mesh)
  return mesh
}

function addCylinderBetween(
  parent: THREE.Object3D,
  start: THREE.Vector3,
  end: THREE.Vector3,
  radius: number,
  material: THREE.Material,
  segments = 16,
) {
  const direction = new THREE.Vector3().subVectors(end, start)
  const mesh = new THREE.Mesh(
    new THREE.CylinderGeometry(radius, radius, direction.length(), segments),
    material,
  )
  mesh.position.copy(start).add(end).multiplyScalar(0.5)
  mesh.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), direction.normalize())
  mesh.castShadow = true
  parent.add(mesh)
  return mesh
}

function createWheelLoader(): { root: THREE.Group; layers: ModelLayers } {
  const yellow = new THREE.MeshStandardMaterial({ color: 0xe7a91f, roughness: 0.48, metalness: 0.12 })
  const yellowLight = new THREE.MeshStandardMaterial({ color: 0xf3bd34, roughness: 0.42, metalness: 0.08 })
  const dark = new THREE.MeshStandardMaterial({ color: 0x202827, roughness: 0.62, metalness: 0.18 })
  const glass = new THREE.MeshStandardMaterial({ color: 0x253d42, roughness: 0.24, metalness: 0.32 })
  const rubber = new THREE.MeshStandardMaterial({ color: 0x202322, roughness: 0.86 })
  const steel = new THREE.MeshStandardMaterial({ color: 0x99a09a, roughness: 0.34, metalness: 0.78 })
  const lamp = new THREE.MeshStandardMaterial({ color: 0xffe6a3, emissive: 0xc98a1a, emissiveIntensity: 0.55 })
  const root = new THREE.Group()
  const body = new THREE.Group()
  const wheels = new THREE.Group()
  const cab = new THREE.Group()
  const loader = new THREE.Group()
  root.add(body, wheels, cab, loader)

  addBox(body, [1.32, 0.56, 2.45], [0, 0.91, -0.02], dark)
  addBox(body, [1.22, 0.82, 1.16], [0, 1.37, 0.79], yellow)
  addBox(body, [1.16, 0.91, 1.2], [0, 1.43, -1.02], yellowLight)
  addBox(body, [1.39, 0.2, 0.82], [0, 1.04, -0.1], yellowLight)
  addBox(body, [0.35, 0.16, 0.38], [0, 1.04, 0.0], yellow)

  const pivot = new THREE.Mesh(new THREE.CylinderGeometry(0.3, 0.3, 0.92, 24), steel)
  pivot.position.set(0, 0.9, -0.08)
  pivot.castShadow = true
  body.add(pivot)
  addBox(body, [0.12, 0.64, 0.38], [0, 1.52, -0.11], dark)

  for (const z of [1.03, -1.04]) {
    for (const x of [-0.77, 0.77]) {
      const tire = new THREE.Group()
      tire.position.set(x, 0.61, z)
      wheels.add(tire)

      const tireBody = new THREE.Mesh(new THREE.CylinderGeometry(0.54, 0.54, 0.39, 32), rubber)
      tireBody.rotation.z = Math.PI / 2
      tireBody.castShadow = true
      tire.add(tireBody)

      for (let treadIndex = 0; treadIndex < 20; treadIndex += 1) {
        const angle = (treadIndex / 20) * Math.PI * 2
        const tread = new THREE.Mesh(new THREE.BoxGeometry(0.42, 0.105, 0.14), dark)
        tread.position.set(0, Math.cos(angle) * 0.515, Math.sin(angle) * 0.515)
        tread.rotation.x = angle
        tread.castShadow = true
        tire.add(tread)
      }

      const rim = new THREE.Mesh(new THREE.CylinderGeometry(0.32, 0.32, 0.42, 32), steel)
      rim.rotation.z = Math.PI / 2
      tire.add(rim)
      const hub = new THREE.Mesh(new THREE.CylinderGeometry(0.22, 0.22, 0.45, 24), yellowLight)
      hub.rotation.z = Math.PI / 2
      tire.add(hub)
      const hubCap = new THREE.Mesh(new THREE.CylinderGeometry(0.115, 0.115, 0.47, 20), dark)
      hubCap.rotation.z = Math.PI / 2
      tire.add(hubCap)
    }
  }

  addBox(cab, [0.94, 1.22, 1.16], [0, 2.02, -0.69], dark)
  addBox(cab, [0.81, 0.7, 0.035], [0, 2.15, -0.08], glass, [-0.05, 0, 0])
  addBox(cab, [0.84, 0.78, 0.04], [0, 2.14, -1.29], glass)
  addBox(cab, [0.035, 0.75, 0.82], [-0.48, 2.13, -0.68], glass)
  addBox(cab, [0.035, 0.75, 0.82], [0.48, 2.13, -0.68], glass)
  addBox(cab, [1.08, 0.13, 1.28], [0, 2.67, -0.68], yellowLight)
  addBox(cab, [0.2, 0.08, 0.2], [0, 2.78, -0.68], dark)
  addBox(cab, [0.08, 0.32, 0.08], [-0.56, 1.82, -0.36], dark)
  addBox(cab, [0.08, 0.32, 0.08], [0.56, 1.82, -0.36], dark)
  addBox(cab, [0.15, 0.06, 0.1], [0.66, 2.08, -0.33], dark)
  addBox(cab, [0.15, 0.06, 0.1], [-0.66, 2.08, -0.33], dark)

  const beacon = new THREE.Mesh(new THREE.CylinderGeometry(0.085, 0.085, 0.13, 16), lamp)
  beacon.position.set(0.34, 2.8, -0.7)
  cab.add(beacon)

  for (const side of [-1, 1]) {
    const armStart = new THREE.Vector3(side * 0.56, 1.48, 0.28)
    const armEnd = new THREE.Vector3(side * 0.72, 1.82, 2.34)
    addCylinderBetween(loader, armStart, armEnd, 0.105, yellowLight, 8)
    addCylinderBetween(
      loader,
      new THREE.Vector3(side * 0.58, 1.36, 0.64),
      new THREE.Vector3(side * 0.72, 1.66, 2.22),
      0.042,
      dark,
      10,
    )
    addCylinderBetween(
      loader,
      new THREE.Vector3(side * 0.53, 1.5, 0.55),
      new THREE.Vector3(side * 0.68, 1.82, 1.83),
      0.035,
      steel,
      12,
    )
  }

  addBox(loader, [1.72, 0.15, 0.78], [0, 0.91, 2.36], dark, [-0.08, 0, 0])
  addBox(loader, [1.65, 0.14, 0.12], [0, 1.18, 2.03], steel)
  addBox(loader, [0.13, 0.52, 0.72], [-0.79, 1.13, 2.63], yellow, [0.04, 0, -0.04])
  addBox(loader, [0.13, 0.52, 0.72], [0.79, 1.13, 2.63], yellow, [0.04, 0, 0.04])
  addBox(loader, [1.56, 0.43, 0.12], [0, 1.2, 2.91], yellowLight, [-0.1, 0, 0])
  addBox(loader, [1.65, 0.12, 0.25], [0, 0.9, 2.94], steel)

  for (let toothIndex = 0; toothIndex < 7; toothIndex += 1) {
    const x = -0.68 + toothIndex * 0.227
    addBox(loader, [0.09, 0.12, 0.28], [x, 0.82, 3.08], steel, [-0.18, 0, 0])
  }

  for (const x of [-0.42, 0.42]) {
    addBox(body, [0.24, 0.14, 0.08], [x, 1.55, 1.39], lamp)
    addBox(body, [0.21, 0.12, 0.06], [x, 1.19, 1.39], dark)
  }

  const exhaust = new THREE.Mesh(new THREE.CylinderGeometry(0.065, 0.075, 0.67, 14), dark)
  exhaust.position.set(-0.4, 1.91, -1.24)
  exhaust.castShadow = true
  body.add(exhaust)
  addBox(body, [0.09, 0.08, 0.08], [-0.4, 2.26, -1.24], steel)

  return { root, layers: { wheels, cab, loader } }
}

export function WheelLoaderViewer() {
  const mountRef = useRef<HTMLDivElement>(null)
  const cameraRef = useRef<THREE.PerspectiveCamera | null>(null)
  const controlsRef = useRef<OrbitControls | null>(null)
  const layersRef = useRef<ModelLayers | null>(null)
  const [view, setView] = useState<ViewPreset>('isometric')
  const [autoRotate, setAutoRotate] = useState(false)
  const [visibleLayers, setVisibleLayers] = useState<Record<LayerName, boolean>>({
    wheels: true,
    cab: true,
    loader: true,
  })
  const [webglError, setWebglError] = useState(false)

  useEffect(() => {
    const host = mountRef.current
    if (!host) return

    let renderer: THREE.WebGLRenderer
    try {
      renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false })
    } catch {
      setWebglError(true)
      return
    }

    const scene = new THREE.Scene()
    scene.background = new THREE.Color(0xd6d5cc)
    scene.fog = new THREE.Fog(0xd6d5cc, 16, 34)
    const camera = new THREE.PerspectiveCamera(34, 1, 0.1, 80)
    camera.position.set(6.7, 4.65, 7.8)
    cameraRef.current = camera

    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2))
    renderer.shadowMap.enabled = true
    renderer.shadowMap.type = THREE.PCFSoftShadowMap
    renderer.outputColorSpace = THREE.SRGBColorSpace
    renderer.toneMapping = THREE.ACESFilmicToneMapping
    renderer.toneMappingExposure = 1.18
    renderer.domElement.className = 'loader-canvas'
    renderer.domElement.setAttribute('aria-label', 'Interactive 3D model of an articulated wheel loader')
    host.appendChild(renderer.domElement)

    scene.add(new THREE.HemisphereLight(0xfaf8ed, 0x8c9288, 2.2))
    const keyLight = new THREE.DirectionalLight(0xfff1d3, 3.4)
    keyLight.position.set(-4, 9, 6)
    keyLight.castShadow = true
    keyLight.shadow.mapSize.set(2048, 2048)
    keyLight.shadow.camera.left = -7
    keyLight.shadow.camera.right = 7
    keyLight.shadow.camera.top = 7
    keyLight.shadow.camera.bottom = -7
    scene.add(keyLight)
    const fillLight = new THREE.DirectionalLight(0xdde8e7, 1.4)
    fillLight.position.set(6, 4, -5)
    scene.add(fillLight)

    const floor = new THREE.Mesh(
      new THREE.PlaneGeometry(120, 120),
      new THREE.MeshStandardMaterial({ color: 0xd6d5cc, roughness: 0.95 }),
    )
    floor.rotation.x = -Math.PI / 2
    floor.position.y = -0.015
    floor.receiveShadow = true
    scene.add(floor)

    const grid = new THREE.GridHelper(22, 44, 0x737b70, 0x9ca196)
    const gridMaterial = grid.material as THREE.Material
    gridMaterial.transparent = true
    gridMaterial.opacity = 0.2
    grid.position.y = 0.006
    scene.add(grid)

    const model = createWheelLoader()
    scene.add(model.root)
    layersRef.current = model.layers

    const controls = new OrbitControls(camera, renderer.domElement)
    controls.target.set(0, 1.35, 0.55)
    controls.enableDamping = true
    controls.dampingFactor = 0.06
    controls.minDistance = 4.7
    controls.maxDistance = 15
    controls.maxPolarAngle = Math.PI * 0.48
    controls.autoRotate = autoRotate
    controls.autoRotateSpeed = 0.8
    controlsRef.current = controls

    const resize = () => {
      const width = host.clientWidth
      const height = host.clientHeight
      if (!width || !height) return
      camera.aspect = width / height
      camera.updateProjectionMatrix()
      renderer.setSize(width, height)
    }
    resize()
    const resizeObserver = new ResizeObserver(resize)
    resizeObserver.observe(host)

    let frameId = 0
    const render = () => {
      controls.update()
      renderer.render(scene, camera)
      frameId = window.requestAnimationFrame(render)
    }
    render()

    return () => {
      window.cancelAnimationFrame(frameId)
      resizeObserver.disconnect()
      controls.dispose()
      renderer.dispose()
      model.root.traverse((object) => {
        if (object instanceof THREE.Mesh) {
          object.geometry.dispose()
          if (Array.isArray(object.material)) object.material.forEach((material) => material.dispose())
          else object.material.dispose()
        }
      })
      scene.clear()
      renderer.domElement.remove()
      controlsRef.current = null
      cameraRef.current = null
      layersRef.current = null
    }
  }, [])

  useEffect(() => {
    if (controlsRef.current) controlsRef.current.autoRotate = autoRotate
  }, [autoRotate])

  useEffect(() => {
    if (!layersRef.current) return
    for (const layer of Object.keys(visibleLayers) as LayerName[]) {
      layersRef.current[layer].visible = visibleLayers[layer]
    }
  }, [visibleLayers])

  useEffect(() => {
    document.title = 'Wheel Loader | Factory Fleet'
  }, [])

  const selectView = (preset: ViewPreset) => {
    const camera = cameraRef.current
    const controls = controlsRef.current
    if (!camera || !controls) return

    const positions: Record<ViewPreset, [number, number, number]> = {
      isometric: [6.7, 4.65, 7.8],
      side: [9.5, 2.9, 0.7],
      front: [0.35, 3.2, 10.5],
    }
    camera.position.set(...positions[preset])
    controls.target.set(0, 1.35, 0.55)
    controls.update()
    setView(preset)
  }

  const toggleLayer = (layer: LayerName) => {
    setVisibleLayers((current) => ({ ...current, [layer]: !current[layer] }))
  }

  const resetView = () => selectView('isometric')

  return (
    <main className="loader-page">
      <div className="loader-scene" ref={mountRef} />
      <div className="loader-grain" aria-hidden="true" />

      <header className="loader-topbar">
        <a className="loader-brand" href="/" aria-label="Back to Factory Agent Workflow">
          <span className="loader-brand-mark"><Truck size={19} strokeWidth={2.2} /></span>
          <span className="loader-brand-copy">
            <strong>FACTORY / FIELD SYSTEMS</strong>
            <small>Equipment intelligence</small>
          </span>
        </a>
        <a className="loader-back" href="/">
          <ArrowLeft size={16} aria-hidden="true" />
          <span>Workflow</span>
        </a>
      </header>

      <section className="loader-heading" aria-labelledby="loader-title">
        <p className="loader-eyebrow"><span /> EQUIPMENT MODEL / 01</p>
        <h1 id="loader-title">Wheel loader</h1>
        <p className="loader-lede">Articulated loading machine</p>
        <div className="loader-model-meta">
          <span className="loader-model-code">WL–320</span>
          <span className="loader-meta-divider" />
          <span>Concept model</span>
        </div>
      </section>

      <aside className="loader-inspector" aria-label="Model details">
        <div className="inspector-topline">
          <span className="inspector-icon"><Box size={17} /></span>
          <span>MODEL INSPECTOR</span>
          <span className="inspector-live"><i /> 3D</span>
        </div>
        <div className="inspector-row"><span>Configuration</span><strong>Articulated</strong></div>
        <div className="inspector-row"><span>Attachment</span><strong>General-purpose bucket</strong></div>
        <div className="inspector-rule" />
        <p className="layer-heading">VISIBLE ASSEMBLIES</p>
        <div className="layer-list">
          {([
            ['loader', 'Lift arms + bucket', Box],
            ['wheels', 'Tire assemblies', CircleDot],
            ['cab', 'Operator cab', Eye],
          ] as const).map(([layer, label, Icon]) => (
            <button
              className="layer-toggle"
              key={layer}
              type="button"
              aria-pressed={visibleLayers[layer]}
              onClick={() => toggleLayer(layer)}
            >
              <Icon size={15} aria-hidden="true" />
              <span>{label}</span>
              {visibleLayers[layer] ? <Eye size={14} aria-hidden="true" /> : <EyeOff size={14} aria-hidden="true" />}
            </button>
          ))}
        </div>
      </aside>

      <div className="loader-scene-caption">
        <span className="caption-line" />
        <span>01</span>
        <span>ARTICULATED FRAME</span>
      </div>

      <nav className="loader-view-controls" aria-label="Camera view">
        <span className="view-control-label">CAMERA</span>
        <div className="view-segment" role="group" aria-label="Choose camera angle">
          {(['isometric', 'side', 'front'] as const).map((preset) => (
            <button
              className={view === preset ? 'view-option active' : 'view-option'}
              type="button"
              key={preset}
              aria-pressed={view === preset}
              onClick={() => selectView(preset)}
            >
              {preset === 'isometric' ? '3/4' : preset}
            </button>
          ))}
        </div>
        <button
          className={autoRotate ? 'icon-control selected' : 'icon-control'}
          type="button"
          title={autoRotate ? 'Stop automatic rotation' : 'Start automatic rotation'}
          aria-label={autoRotate ? 'Stop automatic rotation' : 'Start automatic rotation'}
          aria-pressed={autoRotate}
          onClick={() => setAutoRotate((current) => !current)}
        >
          <Rotate3D size={17} aria-hidden="true" />
        </button>
        <button className="icon-control" type="button" title="Reset camera" aria-label="Reset camera" onClick={resetView}>
          <Focus size={17} aria-hidden="true" />
        </button>
      </nav>

      <footer className="loader-footer">
        <div className="loader-footer-label">
          <span className="footer-status" />
          <span>INTERACTIVE MODEL</span>
        </div>
        <p>Drag to orbit <span>/</span> Scroll to zoom <span>/</span> Select an assembly to isolate it</p>
        <span className="loader-footer-index">FLEET VIEW <b>—</b> 01</span>
      </footer>

      {webglError && (
        <div className="loader-webgl-error" role="alert">
          <strong>3D preview unavailable</strong>
          <span>This browser or device could not start WebGL.</span>
        </div>
      )}
    </main>
  )
}