import React, { useMemo, useState } from 'react';
import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer, LineChart, Line, ScatterPlot, Scatter } from 'recharts';

const BenchmarkVisualization = () => {
  const [selectedPayloadSize, setSelectedPayloadSize] = useState('All');
  const [selectedView, setSelectedView] = useState('performance');

  // Parse the benchmark data
  const rawData = `Method	PayloadSize	ProviderType	Mean	Error	Lock Contentions	Allocated
'Concurrent Write Operations'	Large	FileSystem	2,123,359.07 us	31,104,399.624 us	0	2443756.74 KB
'Concurrent Read Operations'	Large	FileSystem	8,884,596.17 us	58,427,668.792 us	0	8162422.02 KB
'Concurrent Mixed Operations'	Large	FileSystem	5,354,032.83 us	40,210,282.173 us	0	5476159.77 KB
'Concurrent Write Operations'	Large	FileSystem	532,689.84 us	30,626.132 us	0	2443755.59 KB
'Concurrent Read Operations'	Large	FileSystem	2,373,179.40 us	185,431.913 us	0	8162427.43 KB
'Concurrent Mixed Operations'	Large	FileSystem	NA	NA	NA	NA
'Concurrent Write Operations'	Large	InMemory	92.23 us	10.574 us	0.4175	18.44 KB
'Concurrent Read Operations'	Large	InMemory	276.07 us	26.733 us	0.0166	25.5 KB
'Concurrent Mixed Operations'	Large	InMemory	239.70 us	12.468 us	1.4751	20.95 KB
'Concurrent Write Operations'	Large	InMemory	474.12 us	14.058 us	8.9395	18.43 KB
'Concurrent Read Operations'	Large	InMemory	424.41 us	11.672 us	0.0317	25.46 KB
'Concurrent Mixed Operations'	Large	InMemory	408.58 us	7.653 us	2.302	20.94 KB
'Concurrent Write Operations'	Large	SQLite	1,987,100.37 us	2,704,807.526 us	0	2443569.38 KB
'Concurrent Read Operations'	Large	SQLite	3,769,728.23 us	677,345.601 us	0	7673813.43 KB
'Concurrent Mixed Operations'	Large	SQLite	3,057,169.70 us	2,623,731.941 us	0	5120884.04 KB
'Concurrent Write Operations'	Large	SQLite	2,028,263.86 us	81,753.257 us	0	2443568.12 KB
'Concurrent Read Operations'	Large	SQLite	3,355,953.87 us	83,837.468 us	0	7673807.41 KB
'Concurrent Mixed Operations'	Large	SQLite	2,653,854.59 us	77,876.279 us	0	5277783.34 KB
'Concurrent Write Operations'	Medium	FileSystem	57,436.28 us	39,989.493 us	0	7231.34 KB
'Concurrent Read Operations'	Medium	FileSystem	88,664.61 us	35,285.726 us	0	18930.47 KB
'Concurrent Mixed Operations'	Medium	FileSystem	62,284.02 us	33,018.566 us	0	13010.16 KB
'Concurrent Write Operations'	Medium	FileSystem	68,686.49 us	1,337.995 us	0	7231.36 KB
'Concurrent Read Operations'	Medium	FileSystem	167,940.94 us	2,496.161 us	0	18929.83 KB
'Concurrent Mixed Operations'	Medium	FileSystem	103,592.88 us	3,314.362 us	0	12981.25 KB
'Concurrent Write Operations'	Medium	InMemory	84.57 us	16.282 us	0.1014	18.45 KB
'Concurrent Read Operations'	Medium	InMemory	273.32 us	7.938 us	0.0112	25.5 KB
'Concurrent Mixed Operations'	Medium	InMemory	238.11 us	7.094 us	1.3264	20.96 KB
'Concurrent Write Operations'	Medium	InMemory	505.32 us	12.756 us	9.2778	18.41 KB
'Concurrent Read Operations'	Medium	InMemory	426.22 us	8.518 us	0.0317	25.45 KB
'Concurrent Mixed Operations'	Medium	InMemory	395.22 us	7.168 us	1.0283	20.92 KB
'Concurrent Write Operations'	Medium	SQLite	463,413.37 us	51,282.309 us	0	7045.66 KB
'Concurrent Read Operations'	Medium	SQLite	485,465.97 us	60,217.365 us	0	17638.5 KB
'Concurrent Mixed Operations'	Medium	SQLite	551,387.47 us	1,437,894.446 us	0	12358.14 KB
'Concurrent Write Operations'	Medium	SQLite	512,387.52 us	10,210.425 us	0	7045.95 KB
'Concurrent Read Operations'	Medium	SQLite	515,469.96 us	10,577.172 us	0	17639.48 KB
'Concurrent Mixed Operations'	Medium	SQLite	598,715.11 us	37,939.440 us	0	12358.09 KB
'Concurrent Write Operations'	Small	FileSystem	34,238.74 us	6,486.770 us	0	1819.83 KB
'Concurrent Read Operations'	Small	FileSystem	70,314.27 us	32,152.915 us	0	3916.68 KB
'Concurrent Mixed Operations'	Small	FileSystem	47,939.74 us	96,823.597 us	0	2767.89 KB
'Concurrent Write Operations'	Small	FileSystem	60,582.12 us	1,994.424 us	0	1819.79 KB
'Concurrent Read Operations'	Small	FileSystem	156,879.84 us	4,496.964 us	0.2857	3916.78 KB
'Concurrent Mixed Operations'	Small	FileSystem	94,969.15 us	1,507.777 us	0.1667	2762.39 KB
'Concurrent Write Operations'	Small	InMemory	298.58 us	47.847 us	4.499	18.45 KB
'Concurrent Read Operations'	Small	InMemory	274.93 us	8.241 us	0.0269	25.5 KB
'Concurrent Mixed Operations'	Small	InMemory	237.86 us	43.980 us	1.5437	20.96 KB
'Concurrent Write Operations'	Small	InMemory	502.13 us	21.143 us	6.3247	18.42 KB
'Concurrent Read Operations'	Small	InMemory	419.24 us	12.772 us	0.0127	25.45 KB
'Concurrent Mixed Operations'	Small	InMemory	396.41 us	7.693 us	0.9832	20.91 KB
'Concurrent Write Operations'	Small	SQLite	776,399.43 us	118,680.484 us	0	1293.23 KB
'Concurrent Read Operations'	Small	SQLite	668,234.30 us	4,975,496.241 us	0	2763.09 KB
'Concurrent Mixed Operations'	Small	SQLite	830,743.13 us	8,389,639.997 us	0	1949.61 KB
'Concurrent Write Operations'	Small	SQLite	588,289.11 us	51,746.783 us	0	1291.59 KB
'Concurrent Read Operations'	Small	SQLite	639,239.75 us	57,677.608 us	0	2765.01 KB
'Concurrent Mixed Operations'	Small	SQLite	569,192.92 us	29,647.141 us	0	1949.94 KB`;

  const processedData = useMemo(() => {
    const lines = rawData.trim().split('\n');
    const cleanedData = [];
    
    for (let i = 1; i < lines.length; i++) {
      const values = lines[i].split('\t');
      if (values.length >= 7) {
        const row = {
          Method: values[0].replace(/'/g, ''),
          PayloadSize: values[1],
          ProviderType: values[2],
          Mean: values[3],
          Error: values[4],
          LockContentions: values[5],
          Allocated: values[6]
        };
        
        if (row.Mean && row.Mean !== 'NA') {
          const meanStr = row.Mean.toString().replace(/,/g, '').replace(' us', '');
          row.MeanNumeric = parseFloat(meanStr);
          row.MeanMs = row.MeanNumeric / 1000;
        }
        
        if (row.Allocated && row.Allocated !== 'NA') {
          const allocStr = row.Allocated.toString().replace(/,/g, '').replace(' KB', '').replace('\r', '');
          row.AllocatedMB = parseFloat(allocStr) / 1024;
        }

        if (row.LockContentions && row.LockContentions !== 'NA') {
          row.LockContentionsNum = parseFloat(row.LockContentions.toString().replace('\r', ''));
        }
        
        cleanedData.push(row);
      }
    }
    
    return cleanedData.filter(row => row.MeanNumeric && !isNaN(row.MeanNumeric) && row.MeanNumeric > 0);
  }, []);

  // Filter data based on selected payload size
  const filteredData = useMemo(() => {
    if (selectedPayloadSize === 'All') return processedData;
    return processedData.filter(row => row.PayloadSize === selectedPayloadSize);
  }, [processedData, selectedPayloadSize]);

  // Performance comparison data
  const performanceChartData = useMemo(() => {
    const grouped = {};
    
    filteredData.forEach(row => {
      const key = row.Method.replace('Concurrent ', '').replace(' Operations', '');
      if (!grouped[key]) {
        grouped[key] = { name: key };
      }
      grouped[key][row.ProviderType] = row.MeanMs;
    });
    
    return Object.values(grouped);
  }, [filteredData]);

  // Contention and Allocation comparison data
  const contentionAllocationData = useMemo(() => {
    const grouped = {};
    
    filteredData.forEach(row => {
      const key = `${row.Method.replace('Concurrent ', '').replace(' Operations', '')} (${row.ProviderType})`;
      if (!grouped[key]) {
        grouped[key] = {
          name: key,
          provider: row.ProviderType,
          operation: row.Method.replace('Concurrent ', '').replace(' Operations', '')
        };
      }
      grouped[key].lockContentions = row.LockContentionsNum || 0;
      grouped[key].allocatedMB = row.AllocatedMB || 0;
    });
    
    return Object.values(grouped);
  }, [filteredData]);

  // Average data for summary cards
  const averageData = useMemo(() => {
    const providerStats = {};
    
    filteredData.forEach(row => {
      if (!providerStats[row.ProviderType]) {
        providerStats[row.ProviderType] = {
          totalTime: 0,
          totalAllocation: 0,
          totalContentions: 0,
          count: 0
        };
      }
      
      const stats = providerStats[row.ProviderType];
      stats.totalTime += row.MeanMs;
      stats.totalAllocation += row.AllocatedMB || 0;
      stats.totalContentions += row.LockContentionsNum || 0;
      stats.count += 1;
    });

    return Object.entries(providerStats).map(([provider, stats]) => ({
      provider,
      avgTime: stats.totalTime / stats.count,
      avgAllocation: stats.totalAllocation / stats.count,
      avgContentions: stats.totalContentions / stats.count,
      count: stats.count
    }));
  }, [filteredData]);

  const providerColors = {
    InMemory: '#10b981',
    FileSystem: '#f59e0b', 
    SQLite: '#ef4444'
  };

  const formatTime = (value) => {
    if (value >= 1000000) return `${(value / 1000000).toFixed(1)}s`;
    if (value >= 1000) return `${(value / 1000).toFixed(1)}s`;
    return `${value.toFixed(0)}ms`;
  };

  const formatAllocation = (value) => {
    if (value >= 1024) return `${(value / 1024).toFixed(1)}GB`;
    return `${value.toFixed(1)}MB`;
  };

  const CustomTooltip = ({ active, payload, label }) => {
    if (active && payload && payload.length) {
      return (
        <div className="bg-white p-4 border border-gray-200 rounded-lg shadow-lg">
          <p className="font-semibold text-gray-800 mb-2">{label}</p>
          {payload.map((entry, index) => (
            <p key={index} style={{ color: entry.color }} className="text-sm">
              {entry.dataKey}: {
                entry.dataKey === 'lockContentions' ? entry.value.toFixed(2) :
                entry.dataKey === 'allocatedMB' ? formatAllocation(entry.value) :
                formatTime(entry.value)
              }
            </p>
          ))}
        </div>
      );
    }
    return null;
  };

  const payloadSizes = ['All', 'Small', 'Medium', 'Large'];

  return (
    <div className="w-full max-w-7xl mx-auto p-6 bg-gray-50">
      <div className="mb-8">
        <h1 className="text-3xl font-bold text-gray-800 mb-4">
          Persistence Provider Benchmark Analysis
        </h1>
        <p className="text-lg text-gray-600 mb-6">
          Comprehensive performance, contention, and allocation analysis across providers.
        </p>
        
        <div className="flex flex-wrap items-center gap-4 mb-6">
          <div className="flex items-center space-x-2">
            <label htmlFor="payloadSize" className="text-sm font-medium text-gray-700">
              Payload Size:
            </label>
            <select
              id="payloadSize"
              value={selectedPayloadSize}
              onChange={(e) => setSelectedPayloadSize(e.target.value)}
              className="px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
            >
              {payloadSizes.map(size => (
                <option key={size} value={size}>{size}</option>
              ))}
            </select>
          </div>
          
          <div className="flex space-x-2">
            <button
              onClick={() => setSelectedView('performance')}
              className={`px-4 py-2 rounded-lg font-medium transition-colors ${
                selectedView === 'performance'
                  ? 'bg-blue-600 text-white'
                  : 'bg-white text-gray-700 hover:bg-gray-100'
              }`}
            >
              Performance
            </button>
            <button
              onClick={() => setSelectedView('resources')}
              className={`px-4 py-2 rounded-lg font-medium transition-colors ${
                selectedView === 'resources'
                  ? 'bg-blue-600 text-white'
                  : 'bg-white text-gray-700 hover:bg-gray-100'
              }`}
            >
              Contention & Allocation
            </button>
          </div>
        </div>

        {/* Summary Cards */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-8">
          {averageData.map(({ provider, avgTime, avgAllocation, avgContentions, count }) => (
            <div key={provider} className="bg-white p-4 rounded-lg shadow-md">
              <div className="flex items-center justify-between mb-2">
                <h3 className="font-semibold text-gray-800">{provider}</h3>
                <div 
                  className="w-4 h-4 rounded-full" 
                  style={{ backgroundColor: providerColors[provider] }}
                ></div>
              </div>
              <div className="space-y-1 text-sm text-gray-600">
                <p>Avg Time: <span className="font-medium">{formatTime(avgTime)}</span></p>
                <p>Avg Allocation: <span className="font-medium">{formatAllocation(avgAllocation)}</span></p>
                <p>Avg Contentions: <span className="font-medium">{avgContentions.toFixed(2)}</span></p>
                <p className="text-xs text-gray-500">({count} operations)</p>
              </div>
            </div>
          ))}
        </div>
      </div>

      {selectedView === 'performance' && (
        <div className="space-y-8">
          <div className="bg-white p-6 rounded-lg shadow-lg">
            <h2 className="text-xl font-semibold text-gray-800 mb-4">
              Performance Comparison by Provider
              {selectedPayloadSize !== 'All' && (
                <span className="text-base font-normal text-gray-600 ml-2">
                  ({selectedPayloadSize} Payload)
                </span>
              )}

          {selectedPayloadSize !== 'All' && (
            <div className="bg-white p-6 rounded-lg shadow-lg">
              <h2 className="text-xl font-semibold text-gray-800 mb-4">
                Detailed Performance Breakdown - {selectedPayloadSize} Payload
              </h2>
              <div className="h-80">
                <ResponsiveContainer width="100%" height="100%">
                  <BarChart 
                    data={filteredData.map(row => ({
                      name: `${row.Method.replace('Concurrent ', '').replace(' Operations', '')} (${row.ProviderType})`,
                      provider: row.ProviderType,
                      operation: row.Method.replace('Concurrent ', '').replace(' Operations', ''),
                      time: row.MeanMs,
                      fill: providerColors[row.ProviderType]
                    }))}
                    margin={{ top: 20, right: 30, left: 40, bottom: 80 }}
                  >
                    <CartesianGrid strokeDasharray="3 3" />
                    <XAxis 
                      dataKey="name" 
                      angle={-45} 
                      textAnchor="end" 
                      height={100}
                      fontSize={10}
                    />
                    <YAxis 
                      scale="log" 
                      domain={['dataMin', 'dataMax']}
                      tickFormatter={formatTime}
                    />
                    <Tooltip 
                      content={({ active, payload }) => {
                        if (active && payload && payload.length) {
                          const data = payload[0].payload;
                          return (
                            <div className="bg-white p-4 border border-gray-200 rounded-lg shadow-lg">
                              <p className="font-semibold text-gray-800 mb-2">{data.operation} - {data.provider}</p>
                              <p className="text-sm">Time: {formatTime(data.time)}</p>
                            </div>
                          );
                        }
                        return null;
                      }}
                    />
                    <Bar dataKey="time" fill="#8884d8" />
                  </BarChart>
                </ResponsiveContainer>
              </div>
            </div>
          )}
            </h2>
            <div className="h-96">
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={performanceChartData} margin={{ top: 20, right: 30, left: 40, bottom: 20 }}>
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis dataKey="name" />
                  <YAxis 
                    scale="log" 
                    domain={['dataMin', 'dataMax']}
                    tickFormatter={formatTime}
                  />
                  <Tooltip content={<CustomTooltip />} />
                  <Legend />
                  <Bar dataKey="InMemory" fill={providerColors.InMemory} name="InMemory" />
                  <Bar dataKey="FileSystem" fill={providerColors.FileSystem} name="FileSystem" />
                  <Bar dataKey="SQLite" fill={providerColors.SQLite} name="SQLite" />
                </BarChart>
              </ResponsiveContainer>
            </div>
          </div>

          {selectedPayloadSize === 'All' && (
            <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
              {['Small', 'Medium', 'Large'].map(size => {
                // Filter data for this specific size
                const sizeData = processedData.filter(d => d.PayloadSize === size);
                
                // Group by operation type
                const sizeGrouped = {};
                sizeData.forEach(row => {
                  const operationType = row.Method.replace('Concurrent ', '').replace(' Operations', '');
                  if (!sizeGrouped[operationType]) {
                    sizeGrouped[operationType] = { name: operationType };
                  }
                  sizeGrouped[operationType][row.ProviderType] = row.MeanMs;
                });
                
                const chartData = Object.values(sizeGrouped);
                
                return (
                  <div key={size} className="bg-white p-6 rounded-lg shadow-lg">
                    <h3 className="text-lg font-semibold text-gray-800 mb-4">
                      {size} Payload Performance
                    </h3>
                    <div className="text-xs text-gray-500 mb-2">
                      Operations: {chartData.length} | Records: {sizeData.length}
                    </div>
                    <div className="h-64">
                      <ResponsiveContainer width="100%" height="100%">
                        <BarChart
                          data={chartData}
                          margin={{ top: 20, right: 10, left: 20, bottom: 40 }}
                        >
                          <CartesianGrid strokeDasharray="3 3" />
                          <XAxis 
                            dataKey="name" 
                            fontSize={10}
                            interval={0}
                          />
                          <YAxis 
                            scale="log" 
                            domain={['dataMin', 'dataMax']}
                            tickFormatter={formatTime} 
                            fontSize={10} 
                          />
                          <Tooltip content={<CustomTooltip />} />
                          <Legend fontSize={10} />
                          <Bar dataKey="InMemory" fill={providerColors.InMemory} name="InMemory" />
                          <Bar dataKey="FileSystem" fill={providerColors.FileSystem} name="FileSystem" />
                          <Bar dataKey="SQLite" fill={providerColors.SQLite} name="SQLite" />
                        </BarChart>
                      </ResponsiveContainer>
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      )}

      {selectedView === 'resources' && (
        <div className="space-y-8">
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-8">
            <div className="bg-white p-6 rounded-lg shadow-lg">
              <h2 className="text-xl font-semibold text-gray-800 mb-4">
                Lock Contentions by Operation
                {selectedPayloadSize !== 'All' && (
                  <span className="text-base font-normal text-gray-600 ml-2">
                    ({selectedPayloadSize})
                  </span>
                )}
              </h2>
              <div className="h-80">
                <ResponsiveContainer width="100%" height="100%">
                  <BarChart
                    data={contentionAllocationData}
                    margin={{ top: 20, right: 30, left: 20, bottom: 60 }}
                  >
                    <CartesianGrid strokeDasharray="3 3" />
                    <XAxis 
                      dataKey="name" 
                      angle={-45} 
                      textAnchor="end" 
                      height={80}
                      fontSize={10}
                    />
                    <YAxis />
                    <Tooltip content={<CustomTooltip />} />
                    <Bar 
                      dataKey="lockContentions" 
                      fill="#8884d8" 
                      name="Lock Contentions"
                    />
                  </BarChart>
                </ResponsiveContainer>
              </div>
            </div>

            <div className="bg-white p-6 rounded-lg shadow-lg">
              <h2 className="text-xl font-semibold text-gray-800 mb-4">
                Memory Allocation by Operation
                {selectedPayloadSize !== 'All' && (
                  <span className="text-base font-normal text-gray-600 ml-2">
                    ({selectedPayloadSize})
                  </span>
                )}
              </h2>
              <div className="h-80">
                <ResponsiveContainer width="100%" height="100%">
                  <BarChart
                    data={contentionAllocationData}
                    margin={{ top: 20, right: 30, left: 20, bottom: 60 }}
                  >
                    <CartesianGrid strokeDasharray="3 3" />
                    <XAxis 
                      dataKey="name" 
                      angle={-45} 
                      textAnchor="end" 
                      height={80}
                      fontSize={10}
                    />
                    <YAxis tickFormatter={formatAllocation} />
                    <Tooltip content={<CustomTooltip />} />
                    <Bar 
                      dataKey="allocatedMB" 
                      fill="#82ca9d" 
                      name="Allocated Memory (MB)"
                    />
                  </BarChart>
                </ResponsiveContainer>
              </div>
            </div>
          </div>

          <div className="bg-white p-6 rounded-lg shadow-lg">
            <h2 className="text-xl font-semibold text-gray-800 mb-4">
              Resource Usage Correlation
            </h2>
            <div className="h-96">
              <ResponsiveContainer width="100%" height="100%">
                <ScatterPlot
                  data={contentionAllocationData}
                  margin={{ top: 20, right: 30, left: 40, bottom: 20 }}
                >
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis 
                    dataKey="lockContentions" 
                    name="Lock Contentions"
                    type="number"
                  />
                  <YAxis 
                    dataKey="allocatedMB" 
                    name="Memory (MB)"
                    type="number"
                    tickFormatter={formatAllocation}
                  />
                  <Tooltip 
                    cursor={{ strokeDasharray: '3 3' }}
                    content={({ active, payload }) => {
                      if (active && payload && payload.length) {
                        const data = payload[0].payload;
                        return (
                          <div className="bg-white p-4 border border-gray-200 rounded-lg shadow-lg">
                            <p className="font-semibold text-gray-800 mb-2">{data.name}</p>
                            <p className="text-sm">Lock Contentions: {data.lockContentions}</p>
                            <p className="text-sm">Memory: {formatAllocation(data.allocatedMB)}</p>
                          </div>
                        );
                      }
                      return null;
                    }}
                  />
                  <Scatter 
                    dataKey="allocatedMB" 
                    fill="#8884d8"
                    name="Operations"
                  />
                </ScatterPlot>
              </ResponsiveContainer>
            </div>
          </div>
        </div>
      )}

      <div className="mt-8 bg-white p-6 rounded-lg shadow-lg">
        <h2 className="text-xl font-semibold text-gray-800 mb-4">Key Insights</h2>
        <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
          <div className="space-y-4">
            <div className="p-4 bg-blue-50 rounded-lg">
              <h3 className="font-semibold text-blue-800 mb-2">🔍 Performance Analysis</h3>
              <p className="text-sm text-blue-700">
                InMemory consistently outperforms disk-based solutions by 3-4 orders of magnitude. 
                FileSystem shows high variability, while SQLite provides more predictable performance.
              </p>
            </div>
            <div className="p-4 bg-purple-50 rounded-lg">
              <h3 className="font-semibold text-purple-800 mb-2">🔒 Lock Contentions</h3>
              <p className="text-sm text-purple-700">
                Only InMemory shows lock contentions, primarily during write operations. 
                This indicates active concurrency management in memory-based operations.
              </p>
            </div>
          </div>
          <div className="space-y-4">
            <div className="p-4 bg-green-50 rounded-lg">
              <h3 className="font-semibold text-green-800 mb-2">💾 Memory Usage</h3>
              <p className="text-sm text-green-700">
                FileSystem and SQLite show similar memory patterns scaling with payload size. 
                InMemory maintains minimal, consistent memory footprint regardless of payload.
              </p>
            </div>
            <div className="p-4 bg-orange-50 rounded-lg">
              <h3 className="font-semibold text-orange-800 mb-2">📊 Scalability</h3>
              <p className="text-sm text-orange-700">
                InMemory scales best with payload size. FileSystem performance degrades significantly 
                with larger payloads, while SQLite maintains more consistent behavior.
              </p>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default BenchmarkVisualization;