import { Navigate, Route, Routes } from 'react-router-dom';
import { useAuth } from './auth';
import Layout from './components/Layout';
import Login from './pages/Login';
import Dashboard from './pages/Dashboard';
import Items from './pages/Items';
import ItemDetail from './pages/ItemDetail';
import ItemTypes from './pages/ItemTypes';
import Locations from './pages/Locations';
import Parties from './pages/Parties';
import Operations from './pages/Operations';
import Stocktakes from './pages/Stocktakes';
import StocktakeDetail from './pages/StocktakeDetail';
import Alerts from './pages/Alerts';
import Rules from './pages/Rules';
import Workflows from './pages/Workflows';
import Stock from './pages/Stock';
import Devices from './pages/Devices';
import Live from './pages/Live';
import Templates from './pages/Templates';
import Tags from './pages/Tags';
import Events from './pages/Events';
import Users from './pages/Users';
import Presence from './pages/Presence';
import Reports from './pages/Reports';
import Integrations from './pages/Integrations';
import LabelDesigner from './pages/LabelDesigner';
import Import from './pages/Import';
import PrintQueue from './pages/PrintQueue';
import SsoCallback from './pages/SsoCallback';
import Cluster from './pages/Cluster';
import MapPage from './pages/Map';
import Notifications from './pages/Notifications';
import Analytics from './pages/Analytics';
import Anomalies from './pages/Anomalies';
import Encoding from './pages/Encoding';
import Audit from './pages/Audit';
import Billing from './pages/Billing';
import Maintenance from './pages/Maintenance';
import Epcis from './pages/Epcis';
import Portal from './pages/Portal';

export default function App() {
  const { user } = useAuth();
  if (!user && window.location.pathname === '/auth/callback') return <SsoCallback />;
  if (!user) return <Login />;
  if (user.portalPartyId) return <Portal />;
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Dashboard />} />
        <Route path="items" element={<Items />} />
        <Route path="items/:id" element={<ItemDetail />} />
        <Route path="item-types" element={<ItemTypes />} />
        <Route path="locations" element={<Locations />} />
        <Route path="parties" element={<Parties />} />
        <Route path="operations" element={<Operations />} />
        <Route path="stocktakes" element={<Stocktakes />} />
        <Route path="stocktakes/:id" element={<StocktakeDetail />} />
        <Route path="alerts" element={<Alerts />} />
        <Route path="rules" element={<Rules />} />
        <Route path="workflows" element={<Workflows />} />
        <Route path="stock" element={<Stock />} />
        <Route path="devices" element={<Devices />} />
        <Route path="live" element={<Live />} />
        <Route path="events" element={<Events />} />
        <Route path="tags" element={<Tags />} />
        <Route path="templates" element={<Templates />} />
        <Route path="users" element={<Users />} />
        <Route path="presence" element={<Presence />} />
        <Route path="reports" element={<Reports />} />
        <Route path="integrations" element={<Integrations />} />
        <Route path="labels" element={<LabelDesigner />} />
        <Route path="print-queue" element={<PrintQueue />} />
        <Route path="import" element={<Import />} />
        <Route path="cluster" element={<Cluster />} />
        <Route path="map" element={<MapPage />} />
        <Route path="notifications" element={<Notifications />} />
        <Route path="analytics" element={<Analytics />} />
        <Route path="anomalies" element={<Anomalies />} />
        <Route path="encoding" element={<Encoding />} />
        <Route path="audit" element={<Audit />} />
        <Route path="billing" element={<Billing />} />
        <Route path="maintenance" element={<Maintenance />} />
        <Route path="epcis" element={<Epcis />} />
        <Route path="auth/callback" element={<Navigate to="/" replace />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}
