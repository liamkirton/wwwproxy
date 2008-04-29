# --------------------------------------------------------------------------------
# WwwProxy\Scripts\ViewState.py
#
# Copyright ©2008 Liam Kirton <liam@int3.ws>
# --------------------------------------------------------------------------------
# ViewState.py
#
# Created: 28/04/2008
# --------------------------------------------------------------------------------

# Parameter Types

# WwwProxy.ProxyRequest
# .Id
# .Pass
# .SkipRemainingHandlers
# .Header
# .Data

# WwwProxy.ProxyResponse
# .Id
# .Completable
# .Pass
# .SkipRemainingHandlers
# .Header
# .Contents
	
# --------------------------------------------------------------------------------

import clr
import re

clr.AddReference('mscorlib')
clr.AddReference('System')
clr.AddReference('System.Web')
clr.AddReference('WwwProxy')

from System import *
from System.Text import *
from System.Web import *
from WwwProxy import *

# --------------------------------------------------------------------------------

class WwwProxyFilter(object):

	# ----------------------------------------------------------------------------
	
	def __init__(self):
		pass

	# ----------------------------------------------------------------------------
	
	def pre_request_filter(self, request):
		request_match = re.compile(r'^([A-Z]+)\s+(.*)\s+HTTP/\d\.\d').match(request.Header)
		if request_match != None:
			# Print request
			request_groups = request_match.groups()
			print '>>>>> pre_request_filter(%d, %s, %s)' % (request.Id, request_groups[0], request_groups[1])
			print '\n%s\n' % request.Header
			
			# Remove non-essential headers
			remove_headers = ['Accept', 'Accept-Language', 'UA-CPU']
			for h in remove_headers:
				request.Header = re.compile(h + '\\:\\s+.*[\r\n]*', re.I | re.M).sub('', request.Header)
				
			# Replace User-Agent
			user_agent_re = re.compile('(User-Agent\\:\\s+)(.*)([\r\n]*)', re.I | re.M)
			old_user_agent_search = user_agent_re.search(request.Header)
			if old_user_agent_search != None:
				old_user_agent = old_user_agent_search.groups()[1]
				
				# !!! Modify User-Agent here !!!
				new_user_agent = 'Mozilla/4.0 (compatible; WwwProxy 1.2.2.1 (http://int3.ws/); WwwProxyScripting.dll; IronPython)'
				
				new_user_agent_sub = r'\g<1>' + new_user_agent
				if len(old_user_agent_search.groups()[2]) != 0:
					new_user_agent_sub += r'\g<3>'
				request.Header = user_agent_re.sub(new_user_agent_sub, request.Header)
			
			# Automatically pass uninteresting requests, without notifying chained
			# event handlers
			uninteresting_request_types = ['.bmp', '.gif', '.ico', '.jpg', '.png', '.css', '.js']
			request_type = request_groups[1].split('?')[0]
			request_type = request_type.lower()
			for s in uninteresting_request_types:
				if request_type.endswith(s):
					request.Pass = True
					request.SkipRemainingHandlers = True
					break
			
			# Parse POST data
			if request.Data != None and not request.SkipRemainingHandlers:
				if re.compile(r'.+=.+&*').match(request.Data) != None:
					# Parse, modify and rebuild POST data
					new_request_data = ''
					filter_viewstate_data = ''
					
					for s in re.compile(r'&', re.I | re.M).split(request.Data):
						p,v = s.split('=')
						print '\"%s\"=\"%s\"' % (p, v)
						
						# !!! Modify POST "p=v" pairs here !!!
						if p == "__VIEWSTATE":
							filter_viewstate_data = v
						else:
							if len(new_request_data) != 0:
								new_request_data += '&'
							new_request_data += p + '=' + v
					
					request.Data = new_request_data
					
					# Expand __VIEWSTATE
					if filter_viewstate_data != '':
						new_viewstate_data = "\r\n\r\n[WwwProxy __VIEWSTATE Expansion]\r\n\r\n"
						vs_url_decoded = HttpUtility.UrlDecode(filter_viewstate_data)
						vs_url_decoded_bytes = Convert.FromBase64String(vs_url_decoded)
						filter_viewstate_data = HttpUtility.UrlEncode(vs_url_decoded_bytes)
						new_viewstate_data += filter_viewstate_data
						request.Data += new_viewstate_data
						
						print new_viewstate_data
					
					print ''
				else:
					print '%s\n' % request.Data

	# ----------------------------------------------------------------------------

	def post_request_filter(self, request):
		request_match = re.compile(r'^([A-Z]+)\s+(.*)\s+HTTP/\d\.\d').match(request.Header)
		if request_match != None:
			request_groups = request_match.groups()
			
			print '>>>>> post_request_filter(%d, %s, %s)' % (request.Id, request_groups[0], request_groups[1])
			print '\n%s\n' % request.Header
			
			# Contract __VIEWSTATE
			if request.Data != None:
				l = request.Data.split('[WwwProxy __VIEWSTATE Expansion]')
				if l != None and len(l) == 2:
					request.Data = l[0].strip()
					vs_url_decoded_bytes = HttpUtility.UrlDecodeToBytes(l[1].strip())
					request.Data = request.Data + "&__VIEWSTATE=" + HttpUtility.UrlEncode(Convert.ToBase64String(vs_url_decoded_bytes))
					print '%s\n' % request.Data
	
	# ----------------------------------------------------------------------------
		
	def pre_response_filter(self, request, response):
		print '<<<<< pre_response_filter'

	# ----------------------------------------------------------------------------
	
	def post_response_filter(self, request, response):
		print '<<<<< post_response_filter'

	# ----------------------------------------------------------------------------

# --------------------------------------------------------------------------------
