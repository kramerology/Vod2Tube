import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import Layout from './components/Layout'
import ChannelsPage from './pages/ChannelsPage'
import VodsPage from './pages/VodsPage'
import SettingsPage from './pages/SettingsPage'

export default function App() {
  return (
    <BrowserRouter>
      <Layout>
        <Routes>
          <Route path="/"         element={<Navigate to="/channels" replace />} />
          <Route path="/channels" element={<ChannelsPage />} />
          <Route path="/vods"     element={<VodsPage />} />
          <Route path="/settings" element={<SettingsPage />} />
          <Route path="*"         element={<Navigate to="/channels" replace />} />
        </Routes>
      </Layout>
    </BrowserRouter>
  )
}
